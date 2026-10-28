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
        private readonly Control        _dragHandle;

        private bool  _dragging;
        private Point _dragStart = Point.Zero;

        public event EventHandler ParseLatestClicked;

        public OilSummaryPanel() {
            Width           = PanelWidth;
            Height          = PanelHeight;
            ShowBorder      = true;
            BackgroundColor = Color.Black * 0.6f;
            Title           = "Who Touched Oils";

            // Invisible hit target over the painted title bar. Panel title clicks
            // often fall through; a child at Y = -HEADER_HEIGHT sits on the header.
            // Drag must use a global mouse-up (WindowBase2 pattern) so releasing
            // outside the handle does not strand / cancel the drag.
            _dragHandle = new DragHandle {
                Parent   = this,
                Location = new Point(0, -HEADER_HEIGHT),
                Size     = new Point(PanelWidth, HEADER_HEIGHT),
                ZIndex   = int.MaxValue / 4
            };

            _dragHandle.LeftMouseButtonPressed += (s, e) => {
                _dragging  = true;
                _dragStart = Input.Mouse.Position;
            };

            GameService.Input.Mouse.LeftMouseButtonReleased += OnGlobalMouseRelease;

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
                Text           = "After a Deimos pull, oil hits are listed here.",
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
                Point nOffset = Input.Mouse.Position - _dragStart;
                Location += nOffset;
                _dragStart = Input.Mouse.Position;
            }

            base.UpdateContainer(gameTime);
        }

        private void OnGlobalMouseRelease(object sender, MouseEventArgs e) {
            _dragging = false;
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
            _dragHandle.Width    = Math.Max(PanelWidth, contentW + ContentRegion.X);
            _dragHandle.Height   = HEADER_HEIGHT;

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

        protected override void DisposeControl() {
            GameService.Input.Mouse.LeftMouseButtonReleased -= OnGlobalMouseRelease;
            base.DisposeControl();
        }

        /// <summary>
        /// Empty control that still captures mouse (unlike a zero-op draw with no hit target).
        /// ClipsBounds=false so it remains hittable outside the panel ContentRegion scissor.
        /// </summary>
        private sealed class DragHandle : Control {
            public DragHandle() {
                ClipsBounds = false;
            }

            protected override CaptureType CapturesInput() => CaptureType.Mouse;

            protected override void Paint(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, Rectangle bounds) {
                // Intentionally empty — hit target only.
            }
        }

    }

}
