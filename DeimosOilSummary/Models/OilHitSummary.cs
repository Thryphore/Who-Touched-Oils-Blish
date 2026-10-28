using System;
using System.Collections.Generic;

namespace DeimosOilSummary.Models {

    internal static class OilNameFormat {
        public static string Display(string characterName, string accountName) {
            bool hasCharacter = !string.IsNullOrWhiteSpace(characterName);
            bool hasAccount   = !string.IsNullOrWhiteSpace(accountName);

            if (hasCharacter && hasAccount) {
                return $"{characterName} \"{accountName}\"";
            }

            if (hasCharacter) {
                return characterName;
            }

            if (hasAccount) {
                return $"\"{accountName}\"";
            }

            return "Unknown";
        }
    }

    public sealed class PlayerOilCount {
        public string CharacterName { get; set; }
        public string AccountName   { get; set; }
        public int    HitCount      { get; set; }

        public string DisplayName => OilNameFormat.Display(CharacterName, AccountName);
    }

    public sealed class OilFirstTouch {
        public int    OilNumber     { get; set; }
        public string CharacterName { get; set; }
        public string AccountName   { get; set; }
        public long   TimeMs        { get; set; }

        public string DisplayName => OilNameFormat.Display(CharacterName, AccountName);
    }

    public sealed class OilHitSummary {
        public string               LogPath      { get; set; }
        public DateTime             LogTime      { get; set; }
        public bool                 Success      { get; set; }
        public string               ErrorMessage { get; set; }
        public List<OilFirstTouch>  FirstTouches { get; set; } = new List<OilFirstTouch>();
        public List<PlayerOilCount> Players      { get; set; } = new List<PlayerOilCount>();
        public int                  TotalHits    { get; set; }
    }

}
