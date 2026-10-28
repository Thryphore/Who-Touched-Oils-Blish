using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DeimosOilSummary.Models;

namespace DeimosOilSummary.Services {

    /// <summary>
    /// Lightweight EVTC/.zevtc reader that counts Rapid Decay (oil) hits on players.
    /// Matches ArcDPS Mechanics Log skill id 37716 (+ newer id 72779).
    /// First-touch per oil uses the oil gadget's src_instid (same idea as Mechanics Log).
    /// </summary>
    public static class EvtcOilParser {

        public const int DeimosSpeciesId = 17154;

        private static readonly HashSet<uint> OilSkillIds = new HashSet<uint> {
            37716u,
            72779u
        };

        private const byte ResultBlock  = 3;
        private const byte ResultEvade  = 4;
        private const byte ResultAbsorb = 6;
        private const byte ResultBlind  = 7;

        // Oil expands ~every 1s; debounce distinct ticks without double-fires.
        private const long HitDebounceMs = 900;

        private const uint PlayerEliteSentinel = 0xFFFFFFFFu;

        // ArcDPS cbtstatechange — src_agent is the player whose state changed.
        private const byte StateChangeUp   = 3; // alive again
        private const byte StateChangeDead = 4;
        private const byte StateChangeDown = 5;

        public static OilHitSummary Parse(string path) {
            var summary = new OilHitSummary {
                LogPath = path,
                LogTime = File.GetLastWriteTime(path)
            };

            try {
                using (Stream raw = OpenEvtcStream(path))
                using (var reader = new BinaryReader(raw, Encoding.UTF8, leaveOpen: false)) {
                    return ParseReader(reader, summary);
                }
            } catch (Exception ex) {
                summary.Success      = false;
                summary.ErrorMessage = ex.Message;
                return summary;
            }
        }

        private static OilHitSummary ParseReader(BinaryReader reader, OilHitSummary summary) {
            byte[] magic = reader.ReadBytes(4);
            if (magic.Length < 4 || magic[0] != (byte)'E' || magic[1] != (byte)'V' ||
                magic[2] != (byte)'T' || magic[3] != (byte)'C') {
                summary.Success      = false;
                summary.ErrorMessage = "Not an EVTC file.";
                return summary;
            }

            reader.ReadBytes(8); // arcdps build date yyyymmdd
            byte revision = reader.ReadByte();
            ushort encounterId = reader.ReadUInt16();
            reader.ReadByte(); // padding

            if (encounterId != DeimosSpeciesId) {
                summary.Success      = false;
                summary.ErrorMessage = $"Not a Deimos log (species {encounterId}).";
                return summary;
            }

            if (revision != 1) {
                summary.Success      = false;
                summary.ErrorMessage = $"Unsupported EVTC revision {revision} (need 1).";
                return summary;
            }

            uint agentCount = reader.ReadUInt32();
            var agentsByAddress = new Dictionary<ulong, AgentInfo>();

            for (uint i = 0; i < agentCount; i++) {
                AgentInfo agent = ReadAgent(reader);
                agentsByAddress[agent.Address] = agent;
            }

            uint skillCount = reader.ReadUInt32();
            for (uint i = 0; i < skillCount; i++) {
                reader.ReadUInt32();
                reader.ReadBytes(64);
            }

            var hitCounts   = new Dictionary<ulong, int>();
            var lastHitTime = new Dictionary<ulong, long>();
            var addressByInstId = new Dictionary<ushort, ulong>();

            // Alive / Downed / Dead per player address. Default = alive until we see a state change.
            var playerStatus = new Dictionary<ulong, PlayerLifeStatus>();

            // First player to take a landing hit from each oil gadget (src_instid).
            var firstTouchByOil = new Dictionary<ushort, OilFirstTouch>();
            var firstTouchOrder = new List<ushort>();

            while (reader.BaseStream.Position + 64 <= reader.BaseStream.Length) {
                long time          = reader.ReadInt64();
                ulong srcAgent     = reader.ReadUInt64();
                ulong dstAgent     = reader.ReadUInt64();
                int value          = reader.ReadInt32();
                int buffDmg        = reader.ReadInt32();
                reader.ReadUInt32(); // overstack
                uint skillId       = reader.ReadUInt32();
                ushort srcInstId   = reader.ReadUInt16();
                ushort dstInstId   = reader.ReadUInt16();
                reader.ReadUInt16(); // src master
                reader.ReadUInt16(); // dst master
                reader.ReadByte();   // iff
                byte buff          = reader.ReadByte();
                byte result        = reader.ReadByte();
                byte isActivation  = reader.ReadByte();
                reader.ReadByte();   // buff remove
                reader.ReadByte();   // ninety
                reader.ReadByte();   // fifty
                reader.ReadByte();   // moving
                byte isStateChange = reader.ReadByte();
                reader.ReadByte();   // flanking
                reader.ReadByte();   // shields
                byte isOffcycle    = reader.ReadByte(); // set when dst was downed at time of event
                reader.ReadUInt32(); // pad

                if (srcInstId != 0 && srcAgent != 0) {
                    addressByInstId[srcInstId] = srcAgent;
                }

                if (dstInstId != 0 && dstAgent != 0) {
                    addressByInstId[dstInstId] = dstAgent;
                }

                // Track downed / dead / up before any oil logic.
                if (isStateChange == StateChangeUp ||
                    isStateChange == StateChangeDead ||
                    isStateChange == StateChangeDown) {
                    if (srcAgent != 0 && agentsByAddress.TryGetValue(srcAgent, out AgentInfo statusAgent) &&
                        statusAgent.IsPlayer) {
                        if (isStateChange == StateChangeUp) {
                            playerStatus[srcAgent] = PlayerLifeStatus.Alive;
                        } else if (isStateChange == StateChangeDown) {
                            playerStatus[srcAgent] = PlayerLifeStatus.Downed;
                        } else {
                            playerStatus[srcAgent] = PlayerLifeStatus.Dead;
                        }
                    }

                    continue;
                }

                if (isStateChange != 0 || isActivation != 0) {
                    continue;
                }

                if (!OilSkillIds.Contains(skillId)) {
                    continue;
                }

                ulong playerAddress = dstAgent;
                if (playerAddress == 0 && dstInstId != 0 &&
                    addressByInstId.TryGetValue(dstInstId, out ulong mapped)) {
                    playerAddress = mapped;
                }

                if (playerAddress == 0 || !agentsByAddress.TryGetValue(playerAddress, out AgentInfo player)) {
                    continue;
                }

                if (!player.IsPlayer) {
                    continue;
                }

                // Skip hits while dead or downed (state events + is_offcycle downed flag).
                if (isOffcycle != 0) {
                    continue;
                }

                if (playerStatus.TryGetValue(playerAddress, out PlayerLifeStatus life) &&
                    life != PlayerLifeStatus.Alive) {
                    continue;
                }

                if (result == ResultBlock || result == ResultEvade ||
                    result == ResultAbsorb || result == ResultBlind) {
                    continue;
                }

                bool isDirect = buff == 0 && value > 0;
                bool isConditionTick = buff == 1 && buffDmg > 0;
                if (!isDirect && !isConditionTick) {
                    continue;
                }

                // First touch for this oil puddle (keyed by oil gadget inst id).
                if (srcInstId != 0 && !firstTouchByOil.ContainsKey(srcInstId)) {
                    firstTouchByOil[srcInstId] = new OilFirstTouch {
                        OilNumber     = firstTouchOrder.Count + 1,
                        CharacterName = player.CharacterName,
                        AccountName   = player.AccountName,
                        TimeMs        = time
                    };
                    firstTouchOrder.Add(srcInstId);
                }

                if (lastHitTime.TryGetValue(playerAddress, out long previous) &&
                    time - previous < HitDebounceMs) {
                    continue;
                }

                lastHitTime[playerAddress] = time;
                if (!hitCounts.ContainsKey(playerAddress)) {
                    hitCounts[playerAddress] = 0;
                }

                hitCounts[playerAddress]++;
            }

            summary.FirstTouches = firstTouchOrder
                .Select(id => firstTouchByOil[id])
                .ToList();

            summary.Players = hitCounts
                .Select(kv => {
                    AgentInfo agent = agentsByAddress[kv.Key];
                    return new PlayerOilCount {
                        CharacterName = agent.CharacterName,
                        AccountName   = agent.AccountName,
                        HitCount      = kv.Value
                    };
                })
                .OrderByDescending(p => p.HitCount)
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            summary.TotalHits = summary.Players.Sum(p => p.HitCount);
            summary.Success   = true;
            return summary;
        }

        private static AgentInfo ReadAgent(BinaryReader reader) {
            ulong address  = reader.ReadUInt64();
            uint prof      = reader.ReadUInt32();
            uint isElite   = reader.ReadUInt32();
            reader.ReadUInt16(); // toughness
            reader.ReadUInt16(); // concentration
            reader.ReadUInt16(); // healing
            reader.ReadUInt16(); // hitbox width
            reader.ReadInt16();  // condition
            reader.ReadUInt16(); // hitbox height
            byte[] nameBytes = reader.ReadBytes(68);

            string rawName = Encoding.UTF8.GetString(nameBytes);
            string[] parts = rawName.Split(new[] { '\0' }, StringSplitOptions.None);

            string character = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            string account   = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            return new AgentInfo {
                Address       = address,
                IsPlayer      = isElite != PlayerEliteSentinel,
                CharacterName = character,
                AccountName   = account
            };
        }

        private static Stream OpenEvtcStream(string path) {
            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            if (ext == ".zevtc" || ext == ".zip") {
                var ms = new MemoryStream();
                using (ZipArchive zip = ZipFile.OpenRead(path)) {
                    ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name));
                    if (entry == null) {
                        throw new InvalidDataException("Empty .zevtc archive.");
                    }

                    using (Stream entryStream = entry.Open()) {
                        entryStream.CopyTo(ms);
                    }
                }

                ms.Position = 0;
                return ms;
            }

            return File.OpenRead(path);
        }

        private enum PlayerLifeStatus {
            Alive  = 0,
            Downed = 1,
            Dead   = 2
        }

        private sealed class AgentInfo {
            public ulong  Address       { get; set; }
            public bool   IsPlayer      { get; set; }
            public string CharacterName { get; set; }
            public string AccountName   { get; set; }
        }

    }

}
