﻿using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using dnlib.IO;

namespace dnExplorer.Controls {
	public class HexViewer : Control {
		VScrollBar scrollBar;
		IImageStream stream;

		long? selBegin;
		long? selStart;
		long? selEnd;
		bool mouseDown;

		Font currentFont;
		Size charSize;

		const int PAD_X = 5;
		const int PAD_Y = 5;

		public Color BorderColor { get; set; }
		public Color HeaderColor { get; set; }
		public Color SelectedForeColor { get; set; }
		public Color SelectedBackColor { get; set; }

		public IImageStream Stream {
			get { return stream; }
			set {
				if (stream != value) {
					stream = value;

					scrollBar.Minimum = 0;
					var max = stream == null ? 0 : (int)stream.Length / 0x10;
					max = Math.Max(max - 8, 0);
					scrollBar.Maximum = max;
					scrollBar.Value = 0;
					selStart = selEnd = null;
					Invalidate();
				}
			}
		}

		public HexViewer() {
			scrollBar = new VScrollBar {
				Dock = DockStyle.Right
			};
			Controls.Add(scrollBar);
			scrollBar.Scroll += OnScroll;

			SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer |
			         ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
			         ControlStyles.ResizeRedraw, true);

			BackColor = SystemColors.Window;
			ForeColor = SystemColors.WindowText;
			BorderColor = SystemColors.ControlText;
			HeaderColor = SystemColors.HotTrack;
			SelectedForeColor = SystemColors.HighlightText;
			SelectedBackColor = SystemColors.Highlight;

			Font = new Font("Consolas", 10);
			Dock = DockStyle.Fill;
		}

		void OnScroll(object sender, ScrollEventArgs e) {
			Invalidate();
		}

		protected override void OnMouseWheel(MouseEventArgs e) {
			var newValue = scrollBar.Value - Math.Sign(e.Delta) * scrollBar.LargeChange;
			if (newValue < scrollBar.Minimum)
				newValue = scrollBar.Minimum;
			else if (newValue > scrollBar.Maximum)
				newValue = scrollBar.Maximum;
			scrollBar.Value = newValue;
			Invalidate();
			base.OnMouseWheel(e);
		}

		void EnsureFontInfo() {
			if (currentFont != Font) {
				currentFont = Font;
				using (var g = CreateGraphics())
					charSize = TextRenderer.MeasureText(g, "W", currentFont, Size.Empty, TextFormatFlags.NoPadding);
			}
		}

		public enum HitType {
			None,
			Hex,
			Space,
			Ascii
		}

		public struct HitTestResult {
			public HitType Type;
			public long Index;
		}

		public HitTestResult HitTest(Point pt) {
			EnsureFontInfo();

			var visibleLines = (ClientSize.Height - PAD_Y * 2 - 2) / charSize.Height;
			var currentLine = (pt.Y - PAD_Y - 1) / charSize.Height - 1;
			if (currentLine < 0 || currentLine >= visibleLines - 1)
				return new HitTestResult();

			var currentIndexBase = (scrollBar.Value + currentLine) * 0x10L;

			int gridX = (pt.X - PAD_X - 3) / charSize.Width;
			if (gridX > 9 && gridX <= 9 + 16 * 3) // Hex area
				return new HitTestResult {
					Index = currentIndexBase + (gridX - 9) / 3,
					Type = (gridX - 9) % 3 != 0 ? HitType.Hex : HitType.Space
				};
			if (gridX > 11 + 16 * 3 && gridX <= 11 + 16 * 3 + 16) // Ascii area
				return new HitTestResult {
					Index = currentIndexBase + (gridX - 12 - 16 * 3),
					Type = HitType.Ascii
				};
			return new HitTestResult();
		}

		protected override void OnMouseDown(MouseEventArgs e) {
			base.OnMouseDown(e);
			Focus();

			var ht = HitTest(e.Location);
			if (ht.Type == HitType.Hex || ht.Type == HitType.Ascii) {
				if (e.Button == MouseButtons.Left) {
					mouseDown = true;
					selBegin = selStart = selEnd = ht.Index;
					Capture = true;
				}
				else if (selStart == null)
					selStart = selEnd = ht.Index;
			}
			else
				selStart = selEnd = null;
			Invalidate();
		}

		protected override void OnMouseMove(MouseEventArgs e) {
			base.OnMouseMove(e);
			if (mouseDown) {
				var ht = HitTest(e.Location);
				if (ht.Type != HitType.None) {
					if (ht.Index > selBegin.Value) {
						selStart = selBegin;
						selEnd = ht.Index;
					}
					else {
						selStart = ht.Index;
						selEnd = selBegin;
					}
				}
				else
					selStart = selEnd = null;
				Invalidate();
			}
		}

		protected override void OnMouseUp(MouseEventArgs e) {
			base.OnMouseUp(e);
			if (e.Button == MouseButtons.Left && mouseDown) {
				mouseDown = false;
				Capture = false;
				Invalidate();
			}
		}

		protected override void OnPaint(PaintEventArgs e) {
			EnsureFontInfo();

			e.Graphics.Clear(BackColor);
			if (stream != null) {
				var currentIndexBase = scrollBar.Value * 0x10L;

				var currentX = PAD_X;
				var currentY = PAD_Y;

				int visibleLines = (ClientSize.Height - PAD_Y * 2 - 2) / charSize.Height;

				const string Header = " Offset    0  1  2  3  4  5  6  7  8  9  A  B  C  D  E  F    Ascii";
				TextRenderer.DrawText(e.Graphics, Header, Font, new Point(currentX, currentY), HeaderColor);
				currentY += charSize.Height + 2;
				visibleLines--;

				var len = (int)Math.Min(visibleLines * 0x10, stream.Length - currentIndexBase);
				byte[] data = new byte[len];
				stream.Position = currentIndexBase;
				stream.Read(data, 0, data.Length);

				int offset = 0;
				for (int i = 0; i < visibleLines; i++) {
					if (offset >= data.Length)
						continue;

					TextRenderer.DrawText(e.Graphics, currentIndexBase.ToString("X8"), Font, new Point(currentX, currentY), HeaderColor);
					currentX += charSize.Width * 10;

					PaintLine(e.Graphics, data, currentIndexBase, offset, currentX, currentY);

					currentX = PAD_X;
					currentY += charSize.Height;
					currentIndexBase += 0x10;
					offset += 0x10;
				}

				var borderBounds = new Rectangle(PAD_X / 2, PAD_Y / 2, (15 + 16 * 3 + 16) * charSize.Width + PAD_X,
					(visibleLines + 1) * charSize.Height + PAD_Y + 2);
				ControlPaint.DrawBorder(e.Graphics, borderBounds, BorderColor, ButtonBorderStyle.Solid);
				using (var pen = new Pen(BorderColor, 1)) {
					var hexX = borderBounds.Left + 10 * charSize.Width;
					e.Graphics.DrawLine(pen, hexX, borderBounds.Top, hexX, borderBounds.Bottom - 1);
					var ascX = borderBounds.Left + (12 + 16 * 3) * charSize.Width;
					e.Graphics.DrawLine(pen, ascX, borderBounds.Top, ascX, borderBounds.Bottom - 1);
					var hdrY = borderBounds.Top + charSize.Height + 2;
					e.Graphics.DrawLine(pen, borderBounds.Left, hdrY, borderBounds.Right - 1, hdrY);
				}
			}

			base.OnPaint(e);
		}

		static bool Overlaps(long aBegin, long aEnd, long bBegin, long bEnd) {
			return aBegin <= bEnd && bBegin <= aEnd;
		}

		static bool Contains(long aBegin, long aEnd, long bBegin, long bEnd) {
			return bBegin >= aBegin && aEnd >= bEnd;
		}

		static bool Contains(long aBegin, long aEnd, long value) {
			return value >= aBegin && aEnd >= value;
		}

		void PaintLine(Graphics g, byte[] data, long index, int offset, int currentX, int currentY) {
			if (selStart == null || selEnd == null) {
				PaintLineFast(g, data, offset, currentX, currentY, false);
				return;
			}

			long datBegin = index;
			long datEnd = index + 0xf;
			bool overlaps = Overlaps(selStart.Value, selEnd.Value, datBegin, datEnd);
			bool contains = Contains(selStart.Value, selEnd.Value, datBegin, datEnd);

			if (!overlaps)
				PaintLineFast(g, data, offset, currentX, currentY, false);
			else if (contains)
				PaintLineFast(g, data, offset, currentX, currentY, true);
			else
				PaintLineSegmented(g, data, index, offset, currentX, currentY);
		}

		void PaintLineFast(Graphics g, byte[] data, int offset, int currentX, int currentY, bool selected) {
			var lineTxt = new StringBuilder();
			for (int i = 0; i < 0x10; i++) {
				if (offset + i < data.Length)
					lineTxt.AppendFormat("{0:X2} ", data[offset + i]);
			}

			lineTxt.Length--;
			if (selected)
				TextRenderer.DrawText(g, lineTxt.ToString(), Font, new Point(currentX, currentY), SelectedForeColor,
					SelectedBackColor, TextFormatFlags.NoPrefix);
			else
				TextRenderer.DrawText(g, lineTxt.ToString(), Font, new Point(currentX, currentY), ForeColor,
					TextFormatFlags.NoPrefix);

			currentX += (16 * 3 + 2) * charSize.Width;

			lineTxt.Length = 0;
			for (int i = 0; i < 0x10; i++) {
				if (offset + i < data.Length) {
					byte dat = data[offset + i];
					if (dat <= 32 || (dat >= 127 && dat < 160))
						lineTxt.Append(".");
					else
						lineTxt.Append((char)dat);
				}
				else
					lineTxt.Append(" ");
			}

			if (selected)
				TextRenderer.DrawText(g, lineTxt.ToString(), Font, new Point(currentX, currentY), SelectedForeColor,
					SelectedBackColor, TextFormatFlags.NoPrefix);
			else
				TextRenderer.DrawText(g, lineTxt.ToString(), Font, new Point(currentX, currentY), ForeColor,
					TextFormatFlags.NoPrefix);
		}

		void PaintLineSegmented(Graphics g, byte[] data, long index, int offset, int currentX, int currentY) {
			var lineTxt = new StringBuilder();
			bool prevSel = Contains(selStart.Value, selEnd.Value, index);
			bool currentSel = prevSel;

			for (int i = 0; i < 0x11; i++) {
				currentSel = Contains(selStart.Value, selEnd.Value, index + i);
				if (currentSel != prevSel || i == 0x10) {
					lineTxt.Length--;
					if (prevSel)
						TextRenderer.DrawText(g, lineTxt.ToString(), Font, new Point(currentX, currentY), SelectedForeColor,
							SelectedBackColor, TextFormatFlags.NoPrefix);
					else
						TextRenderer.DrawText(g, lineTxt.ToString(), Font, new Point(currentX, currentY), ForeColor,
							TextFormatFlags.NoPrefix);
					currentX += (lineTxt.Length + 1) * charSize.Width;
					lineTxt.Length = 0;
					prevSel = currentSel;
					if (i == 0x10)
						break;
				}

				if (offset + i < data.Length)
					lineTxt.AppendFormat("{0:X2} ", data[offset + i]);
				else
					lineTxt.AppendFormat("   ");
			}


			currentX += 2 * charSize.Width;
			prevSel = Contains(selStart.Value, selEnd.Value, index);
			currentSel = prevSel;

			for (int i = 0; i < 0x11; i++) {
				currentSel = Contains(selStart.Value, selEnd.Value, index + i);
				if (currentSel != prevSel || i == 0x10) {
					if (prevSel)
						TextRenderer.DrawText(g, lineTxt.ToString(), Font, new Point(currentX, currentY), SelectedForeColor,
							SelectedBackColor, TextFormatFlags.NoPrefix);
					else
						TextRenderer.DrawText(g, lineTxt.ToString(), Font, new Point(currentX, currentY), ForeColor,
							TextFormatFlags.NoPrefix);
					currentX += lineTxt.Length * charSize.Width;
					lineTxt.Length = 0;
					prevSel = currentSel;
					if (i == 0x10)
						break;
				}

				if (offset + i < data.Length) {
					byte dat = data[offset + i];
					if (dat <= 32 || (dat >= 127 && dat < 160))
						lineTxt.Append(".");
					else
						lineTxt.Append((char)dat);
				}
				else
					lineTxt.Append(" ");
			}
		}
	}
}