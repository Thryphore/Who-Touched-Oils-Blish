using System;
using System.Text;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using DeimosOilSummary.Models;
using Microsoft.Xna.Framework;
using Color = Microsoft.Xna.Framework.Color;

namespace DeimosOilSummary.Controls {

    public sealed class OilSummaryPanel : Panel {

        private const int PanelWidth   = 400;
        private const int PanelHeight  = 420;
        private const int FooterHeight = 44;
        private const int StatusTop    = 6;
        private const int BodyTop      = 40;

        private readonly Label          _statusLabel;
        private readonly Panel          _bodyHost;
        private readonly Label          _bodyLabel;
        private readonly StandardButton _latestButton;
        private readonly StandardButton _dismissButton;
        private readonly Panel          _dragHandle;

        private bool  _dragging;
        private Point _dragStart = Point.Zero;

        public event EventHandler ParseLatestClicked;

        public OilSummaryPanel() {
            Width           = PanelWidth;
            Height          = PanelHeight;
            ShowBorder      = true;
            BackgroundColor = Color.Black * 0.6f;
            Title           = "Who Touched Oils";

            // Children are laid out in ContentRegion (below the title).
            // A handle at Y = -HEADER_HEIGHT sits on the title bar and receives mouse input.
            _dragHandle = new Panel {
                Parent          = this,
                Location        = new Point(0, -HEADER_HEIGHT),
                Size            = new Point(PanelWidth, HEADER_HEIGHT),
                BackgroundColor = Color.Transparent,
                ZIndex          = 100,
                BasicTooltipText = "Drag to move"
            };

            _dragHandle.LeftMouseButtonPressed += (s, e) => {
                _dragging  = true;
                _dragStart = Input.Mouse.Position;
            };

            _dragHandle.LeftMouseButtonReleased += (s, e) => {
                _dragging = false;
            };

            _statusLabel = new Label {
                Text           = "Waiting for a Deimos log…",
                Font           = GameService.Content.DefaultFont14,
                TextColor      = Color.LightGray,
                ShowShadow     = true,
                WrapText       = true,
                AutoSizeHeight = true,
                Location       = new Point(10, StatusTop),
                Parent         = this
            };

            _bodyHost = new Panel {
                ShowBorder      = false,
                CanScroll       = true,
                BackgroundColor = Color.Transparent,
                Parent          = this
            };

            _bodyLabel = new Label {
                Text           = "After a Deimos pull, oil hits are listed here\n(from ArcDPS logs — no bridge needed).",
                Font           = GameService.Content.DefaultFont14,
                TextColor      = Color.White,
                ShowShadow     = true,
                WrapText       = true,
                AutoSizeHeight = true,
                Location       = new Point(4, 2),
                Parent         = _bodyHost
            };

            _latestButton = new StandardButton {
                Text   = "Parse latest Deimos log",
                Width  = 200,
                Height = 28,
                Parent = this
            };

            _dismissButton = new StandardButton {
                Text   = "Hide",
                Width  = 80,
                Height = 28,
                Parent = this
            };

            _latestButton.Click  += (s, e) => ParseLatestClicked?.Invoke(this, EventArgs.Empty);
            _dismissButton.Click += (s, e) => Visible = false;

            Resized += (s, e) => LayoutChildren();
            LayoutChildren();
        }

        public override void UpdateContainer(GameTime gameTime) {
            if (_dragging) {
                if (Input.Mouse.State.LeftButton != Microsoft.Xna.Framework.Input.ButtonState.Pressed) {
                    _dragging = false;
                } else {
                    // Incremental move (same pattern as other Blish modules).
                    Point nOffset = Input.Mouse.Position - _dragStart;
                    Location += nOffset;
                    _dragStart = Input.Mouse.Position;
                }
            }

            base.UpdateContainer(gameTime);
        }

        public void SetStatus(string status) {
            _statusLabel.Text = status ?? string.Empty;
            LayoutChildren();
        }

        public void Apply(OilHitSummary summary) {
            if (summary == null) {
                return;
            }

            if (!summary.Success) {
                _statusLabel.Text = summary.ErrorMessage ?? "Parse failed.";
                _statusLabel.TextColor = Color.Orange;
                LayoutChildren();
                return;
            }

            int firstCount = summary.FirstTouches?.Count ?? 0;
            _statusLabel.TextColor = Color.LightGreen;
            _statusLabel.Text =
                $"{summary.LogTime:g}  ·  {firstCount} oil(s)  ·  {summary.TotalHits} hit(s)";

            bool noHits = (summary.Players == null || summary.Players.Count == 0)
                       && (summary.FirstTouches == null || summary.FirstTouches.Count == 0);

            if (noHits) {
                _bodyLabel.Text = "Nobody stepped in oils this pull.";
                _bodyLabel.TextColor = Color.LightGreen;
                Visible = true;
                LayoutChildren();
                return;
            }

            var sb = new StringBuilder();

            sb.AppendLine("First to touch each oil:");
            if (summary.FirstTouches == null || summary.FirstTouches.Count == 0) {
                sb.AppendLine("  (none detected)");
            } else {
                foreach (OilFirstTouch touch in summary.FirstTouches) {
                    sb.AppendLine($"  Oil {touch.OilNumber}: {touch.DisplayName}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Total oil hits:");
            if (summary.Players == null || summary.Players.Count == 0) {
                sb.AppendLine("  (none)");
            } else {
                int rank = 1;
                foreach (PlayerOilCount player in summary.Players) {
                    string times = player.HitCount == 1 ? "1 time" : $"{player.HitCount} times";
                    sb.AppendLine($"  {rank}. {player.DisplayName} — {times}");
                    rank++;
                }
            }

            _bodyLabel.Text      = sb.ToString().TrimEnd();
            _bodyLabel.TextColor = Color.White;
            Visible = true;
            LayoutChildren();
        }

        private void LayoutChildren() {
            int contentW = Math.Max(100, ContentRegion.Width);
            int contentH = Math.Max(120, ContentRegion.Height);

            _dragHandle.Location = new Point(0, -HEADER_HEIGHT);
            _dragHandle.Size     = new Point(contentW + ContentRegion.X, HEADER_HEIGHT);

            _statusLabel.Width = contentW - 20;

            int bodyBottom = contentH - FooterHeight;
            _bodyHost.Location = new Point(6, BodyTop);
            _bodyHost.Size     = new Point(contentW - 12, Math.Max(60, bodyBottom - BodyTop));

            _bodyLabel.Width = Math.Max(80, _bodyHost.ContentRegion.Width - 16);
            _bodyLabel.RecalculateLayout();

            int buttonY = contentH - 34;
            _latestButton.Location  = new Point(10, buttonY);
            _dismissButton.Location = new Point(220, buttonY);
        }

    }

}
