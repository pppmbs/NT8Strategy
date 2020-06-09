// 
// Copyright (C) 2020, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.DrawingTools
{
	/// <summary>
	/// Represents an interface that exposes information regarding a Text IDrawingTool.
	/// </summary>
	public class Text : DrawingTool
	{
		private		Brush							areaBrush;
		private		DeviceBrush				 		areaBrushDevice			= new DeviceBrush();
		private		int								areaOpacity;
		private		TextAlignment					alignment;
		[CLSCompliant(false)]
		protected	SharpDX.DirectWrite.TextLayout	cachedTextLayout;
		private		Gui.Tools.SimpleFont 			font;
		private		Rect							layoutRect;
		private		bool							needsLayoutUpdate;
		private		readonly	float 				outlinePadding 			= GetPadding();
		private		Brush							textBrush;
		private		DeviceBrush 					textBrushDevice			= new DeviceBrush();
		private		string							text;
		private		Popup							popup;
		
		public override object Icon { get { return Gui.Tools.Icons.DrawText; } }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolTextAlignment", GroupName = "NinjaScriptGeneral", Order = 7)]
		public TextAlignment Alignment
		{
			get { return alignment; }
			set
			{
				if (alignment == value)
					return;
				alignment			= value;
				needsLayoutUpdate	= true;
			}
		}
		
		[XmlIgnore]
		[Browsable(false)]
		public bool	UseChartTextBrush { get; set; }
		
		[Browsable(false)]
		public bool	UseChartTextBrushSerialize
		{
			get { return UseChartTextBrush && (LastBrush == null || TextBrush == null || LastBrush.ToString() == TextBrush.ToString()); }
			set { UseChartTextBrush = value; }
		}
		
		[Browsable(false)]
		[EditorBrowsable(EditorBrowsableState.Never)] 
		public bool ManuallyDrawn { get; set; }
		
		[XmlIgnore]
		[Browsable(false)]
		public Brush LastBrush { get; set; }
		
		public ChartAnchor Anchor { get; set; }

		public override IEnumerable<ChartAnchor> Anchors { get { return new[] { Anchor }; } }

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolShapesAreaBrush", GroupName = "NinjaScriptGeneral", Order = 1)]
		public Brush AreaBrush
		{
			get { return areaBrush; }
			set
			{
				areaBrush = value;
				if (areaBrush != null && areaBrush.CanFreeze)
					areaBrush.Freeze();
			}
		}
		[Browsable(false)]
		public string AreaBrushSerialize
		{
			get { return Serialize.BrushToString(AreaBrush); }
			set { AreaBrush = Serialize.StringToBrush(value); }
		}

		/// <summary>
		/// Opacity in percent value (0 to 100)
		/// </summary>
		[Range(0, 100)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolAreaOpacity", GroupName = "NinjaScriptGeneral", Order = 2)]
		public int AreaOpacity
		{
			get { return areaOpacity; }
			set { areaOpacity = Math.Max(0, Math.Min(100, value)); }
		}

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolTextFont", GroupName = "NinjaScriptGeneral", Order = 4)]
		public Gui.Tools.SimpleFont Font
		{
			get { return font; }
			set
			{
				font				= value;
				needsLayoutUpdate	= true;
			}
		}

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolTextOutlineStroke", GroupName = "NinjaScriptGeneral", Order = 3)]
		public Stroke OutlineStroke { get; set; }

		[ExcludeFromTemplate]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolText", GroupName = "NinjaScriptGeneral", Order = 5)]
		[PropertyEditor("NinjaTrader.Gui.Tools.MultilineEditor")]
		public string DisplayText
		{
			get { return text; }
			set
			{
				if (text == value)
					return;
				text				= value;
				needsLayoutUpdate	= true;
			}
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolTextBrush", GroupName = "NinjaScriptGeneral", Order = 1)]
		public Brush TextBrush
		{
			get { return textBrush; }
			set
			{	
				textBrush = value;
				if (textBrush != null && textBrush.CanFreeze)
					textBrush.Freeze();
			}
		}

		[Browsable(false)]
		public string TextBrushSerialize
		{
			get { return Serialize.BrushToString(TextBrush); }
			set { TextBrush = Serialize.StringToBrush(value); }
		}

		/// <summary>
		///  set this to offset the text up/down by a certain number of pixels
		/// </summary>
		[Browsable(false)]
		public int YPixelOffset { get; set; }

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (cachedTextLayout != null)
				cachedTextLayout.Dispose();
			if (textBrushDevice != null)
				textBrushDevice.RenderTarget = null;
			if (areaBrushDevice != null)
				areaBrushDevice.RenderTarget = null;
			cachedTextLayout	= null;
			textBrushDevice		= null;
			areaBrushDevice		= null;
		}

		private void DrawText(ChartControl chartControl)
		{
			if (Font == null || string.IsNullOrEmpty(DisplayText))
				return;

			Rect				outLineRect		= GetCurrentRect(layoutRect, outlinePadding); // this will add padding to layoutRect for us
			SharpDX.RectangleF	outlineRectDx	= new SharpDX.RectangleF((float)outLineRect.X, (float)outLineRect.Y, (float)outLineRect.Width, (float)outLineRect.Height);
			Stroke				outlineStroke	= OutlineStroke;
			textBrushDevice	.RenderTarget		= RenderTarget;
			areaBrushDevice	.RenderTarget		= RenderTarget;
			outlineStroke	.RenderTarget		= RenderTarget;

			SharpDX.Direct2D1.Brush tmpBrush;
			if (AreaBrush != null)
			{
				SolidColorBrush tmpOb		= areaBrushDevice.Brush as SolidColorBrush;
				SolidColorBrush tmpNb 		= AreaBrush 			as SolidColorBrush;
				// if brush not set, set brush. else if brush set and changed, change brush. if not SolidColorBrush always change brush
				if (tmpNb == null || tmpOb == null || tmpOb.Color != tmpNb.Color || Math.Abs(tmpOb.Opacity - tmpNb.Opacity) > 0.1)
				{
					Brush brushCopy			= AreaBrush.Clone();
					brushCopy.Opacity		= areaOpacity / 100d;
					areaBrushDevice.Brush	= brushCopy;
				}
				areaBrushDevice.RenderTarget	= RenderTarget;
				tmpBrush						= IsInHitTest ? chartControl.SelectionBrush : areaBrushDevice.BrushDX;
				RenderTarget.FillRectangle(outlineRectDx, tmpBrush);
			}
			else 
				areaBrushDevice.RenderTarget = null;

			if (outlineStroke.StrokeStyle != null && (outlineStroke.Brush != null || !outlineStroke.Brush.IsTransparent()))
			{
				tmpBrush = IsInHitTest ? chartControl.SelectionBrush : outlineStroke.BrushDX;
				if (tmpBrush != null)
					RenderTarget.DrawRectangle(outlineRectDx, tmpBrush, outlineStroke.Width, outlineStroke.StrokeStyle);
			}
			
			textBrushDevice.RenderTarget = RenderTarget;
			
			SolidColorBrush tmpOtb = textBrushDevice.Brush  as SolidColorBrush;
			SolidColorBrush tmpNtb = TextBrush				as SolidColorBrush;
			// if brush not set, set brush. else if brush set and changed, change brush. if not SolidColorBrush always change brush
			if (tmpNtb == null || tmpOtb == null || tmpOtb.Color != tmpNtb.Color || Math.Abs(tmpOtb.Opacity - tmpNtb.Opacity) > 0.1)
				textBrushDevice.Brush = TextBrush;
			// when drawing the actual text layout, add padding again, we dont want text right on the edges of our outline rect
			tmpBrush = IsInHitTest ? chartControl.SelectionBrush : textBrushDevice.BrushDX;
			RenderTarget.DrawTextLayout(new SharpDX.Vector2(outlineRectDx.X + outlinePadding, outlineRectDx.Y + outlinePadding),
				cachedTextLayout, tmpBrush, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
		}

		public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
		{
			if (DrawingState == DrawingState.Building)
				return popup == null || !popup.IsOpen ? Cursors.IBeam : null;
			if (DrawingState == DrawingState.Moving)
				return IsLocked ? Cursors.No : Cursors.SizeAll;
			// the rect width/height acts as a sensitivity here
			return GetCurrentRect(layoutRect, outlinePadding).IntersectsWith(new Rect(point.X, point.Y, 4, 4)) ? IsLocked ? Cursors.Arrow : Cursors.SizeAll : null;
		}

		protected virtual Rect GetCurrentRect(Rect pLayoutRect, double pOutlinePadding)
		{
			return !ManuallyDrawn 
				? new Rect(pLayoutRect.X - pOutlinePadding, pLayoutRect.Y - pLayoutRect.Height / 2 - pOutlinePadding, pLayoutRect.Width + pOutlinePadding * 2, pLayoutRect.Height + pOutlinePadding * 2)
				: new Rect(pLayoutRect.X - pOutlinePadding, pLayoutRect.Y - pOutlinePadding, pLayoutRect.Width + pOutlinePadding * 2, pLayoutRect.Height + pOutlinePadding * 2);
		}

		private static float GetPadding()
		{
			float? paddingResource = Application.Current.FindResource("FontModalTitleMargin") as float?;
			return paddingResource.HasValue ? paddingResource.Value : 3f;
		}

		protected virtual Point GetTextDrawingPosition(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale)
		{
			// depending on alignment, we need to align text ourselves here
			Point anchorPoint = Anchor.GetPoint(chartControl, chartPanel, chartScale);
			if (cachedTextLayout == null)
				return anchorPoint;

			switch (Alignment)
			{
				case TextAlignment.Center	: return new Point(anchorPoint.X - cachedTextLayout.MaxWidth / 2, anchorPoint.Y);
				case TextAlignment.Right	: return new Point(anchorPoint.X - cachedTextLayout.MaxWidth, anchorPoint.Y);
				case TextAlignment.Left		: return new Point(anchorPoint.X + outlinePadding, anchorPoint.Y);
				default						: return anchorPoint;
			}
		}

		public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			if (DrawingState == DrawingState.Building || layoutRect == default(Rect) || popup != null && popup.IsOpen)
				return new Point[0];

			Rect curRect = GetCurrentRect(layoutRect, outlinePadding);
			return new[] { curRect.TopLeft, curRect.TopRight, curRect.BottomLeft, curRect.BottomRight };
		}

		public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (DrawingState == DrawingState.Building)
				return true;


			// get our width -> time value so we can account for the actual text displayed (since there is only one anchor)
			//chartControl.GetTimeByX
			float startX = chartControl.GetXByTime(Anchor.Time);
			float checkX = startX + (cachedTextLayout == null ? 0 : cachedTextLayout.Metrics.Width);

			DateTime rightWidthTime = chartControl.GetTimeByX((int)checkX);
			// first check we're scrolled horizontally in to view
			if (Anchor.Time > lastTimeOnChart || rightWidthTime < firstTimeOnChart)
				return false;

			if (IsAutoScale)
				return true;

			// even if we're not truely visible, render once so we end up w/ a text layout for measurement
			if (needsLayoutUpdate || cachedTextLayout == null)
				return true;

			// check y bounds as well
			float startY			= chartScale.GetYByValue(Anchor.Price);
			float textHeight		= cachedTextLayout.Metrics.Height;
			double textBottomPrice	= chartScale.GetValueByY(startY + textHeight);
			if (textBottomPrice > chartScale.MaxValue || Anchor.Price < chartScale.MinValue)
				return false;

			return true;
		}

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;

			if (!IsVisible)
				return;

			if (DrawingState != DrawingState.Building)
			{
				MinValue = Anchor.Price;
				MaxValue = Anchor.Price; // y axis
			}
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name			= Custom.Resource.NinjaScriptDrawingToolText;
				Alignment		= TextAlignment.Left;
				Anchor			= new ChartAnchor { IsEditing = true, DrawingTool = this, DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchor };
				Font			= new Gui.Tools.SimpleFont() { Size = 14 };
				OutlineStroke	= new Stroke(Brushes.Transparent, 2f);
				TextBrush		= textBrush;
				AreaBrush		= Brushes.Transparent;
				AreaOpacity		= 100;
				YPixelOffset	= 0;
			}
			else if (State == State.Terminated)
			{
				TextBrush = null;
				textBrush = null;
				Dispose();
			}
		}

		public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (DrawingState == DrawingState.Building)
			{
				dataPoint.CopyDataValues(Anchor);
				Anchor.IsEditing	= false;

				DisplayText			= string.Empty;
				TextBox tb			= new TextBox
				{
					AcceptsReturn		= true,
					Background			= new SolidColorBrush(Color.FromArgb(4, 0, 0, 0)),
					BorderBrush			= chartControl.Properties.AxisPen.Brush,
					FontFamily			= Font.Family,
					FontSize			= Font.Size,
					FontStyle			= Font.Italic ? FontStyles.Italic : FontStyles.Normal,
					FontWeight			= Font.Bold ? FontWeights.Bold : FontWeights.Normal,
					Foreground			= TextBrush ?? chartControl.Properties.ChartText,
					HorizontalAlignment	= HorizontalAlignment.Stretch,
					Style				= Application.Current.FindResource("TextBoxNoEffects") as Style
				};
				
				if (TextBrush == null)
					UseChartTextBrush = true;

				popup = new Popup
				{
					AllowsTransparency	= true,
					PlacementTarget		= chartPanel,
					Placement			= PlacementMode.MousePoint,
					MinWidth			= 75,
					IsOpen				= false,
					StaysOpen			= false,
					Child				= tb
				};
				
				tb.PreviewKeyDown += (sender, args) =>
				{
					if (args.Key == Key.System && args.SystemKey == Key.Enter)
					{
						int		oldIdx	= tb.CaretIndex;
						string	text1	= tb.Text.Substring(0, oldIdx);
						string	text2	= tb.Text.Substring(oldIdx);
						tb.Text			= string.Format("{0}{1}{2}", text1, Environment.NewLine, text2);
						tb.CaretIndex	= oldIdx + Environment.NewLine.Length;
						args.Handled	= true;
					}
					if (args.Key == Key.Enter)
					{
						popup.IsOpen = false;
						args.Handled = true;
					}
				};

				tb.PreviewLostKeyboardFocus += (sender, args) => { popup.IsOpen = false; };

				popup.IsOpen = true;
				ManuallyDrawn = true;
				tb.Focus();

				chartControl.OwnerChart.PreviewMouseDown += OnChartMouseDown;

				popup.Closed += (sender, args) =>
				{
					chartControl.OwnerChart.PreviewMouseDown -= OnChartMouseDown;
					DisplayText		= tb.Text;
					DrawingState	= DrawingState.Normal;
					IsSelected		= false;
					chartControl.InvalidateVisual();
					if (chartControl.IsStayInDrawMode)
						chartControl.TryStartDrawing(GetType().FullName);
				};
			}
			else
			{
				Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
				if (GetCurrentRect(layoutRect, outlinePadding).IntersectsWith(new Rect(point.X, point.Y, 2, 2)))
				{
					Anchor.IsEditing	= true;
					DrawingState		= DrawingState.Moving;
				}
				else
					IsSelected = false;
			}
		}

		private void OnChartMouseDown(object sender, MouseButtonEventArgs e)
		{
			if (popup.IsMouseDirectlyOver)
				return;
			popup.IsOpen = false;
		}

		public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (!IsLocked && (DrawingState == DrawingState.Moving || DrawingState == DrawingState.Editing))
				Anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
		}

		public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			DrawingState = DrawingState.Normal;
		}

		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (DrawingState == DrawingState.Building)
				return;
			
			if (UseChartTextBrush)
			{
				if(!ReferenceEquals(LastBrush, TextBrush) && !ReferenceEquals(LastBrush, chartControl.Properties.ChartText) && LastBrush != null)
				{
					LastBrush = TextBrush;
					UseChartTextBrush = false;
				}
				else
				{
					TextBrush = chartControl.Properties.ChartText;
					LastBrush = TextBrush;
				}
			}
			
			ChartPanel chartPanel = chartControl.ChartPanels[PanelIndex];

			// call update text layout first, in case GetTextDrawingPosition depends on layout (fixed text)
			UpdateTextLayout(chartPanel.W);

			Point txtPoint	= GetTextDrawingPosition(chartControl, chartPanel, chartScale);
			float x			= (float)txtPoint.X;
			float y			= (float)txtPoint.Y;

			// match NT7. A positive value moves the text UP
			y -= YPixelOffset;
			// make sure this is updated befoer DrawText() is called
			layoutRect = new Rect(x, y, cachedTextLayout.MaxWidth, cachedTextLayout.MaxHeight);
			DrawText(chartControl);
		}

		private void UpdateTextLayout(float maxWidth)
		{
			if (!needsLayoutUpdate)
				return;

			needsLayoutUpdate = false;

			cachedTextLayout = null;
			if (Font == null)
				return;

			SharpDX.DirectWrite.TextFormat	textFormat			= Font.ToDirectWriteTextFormat();
											cachedTextLayout	= new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, DisplayText ?? string.Empty, textFormat, maxWidth, textFormat.FontSize);
			// again, make sure to chop max width/height to only amount actually needed
			cachedTextLayout.MaxWidth							= cachedTextLayout.Metrics.Width;
			cachedTextLayout.MaxHeight							= cachedTextLayout.Metrics.Height;
			// NOTE: always use leading alignment since our layout box will be the size of the text (http://i.msdn.microsoft.com/dynimg/IC520425.png)
			cachedTextLayout.TextAlignment						= Alignment == TextAlignment.Center ? SharpDX.DirectWrite.TextAlignment.Center : Alignment == TextAlignment.Right ? SharpDX.DirectWrite.TextAlignment.Trailing : SharpDX.DirectWrite.TextAlignment.Leading;
			needsLayoutUpdate									= false;
			textFormat.Dispose();
		}
	}

	[TypeConverter("NinjaTrader.Custom.ResourceEnumConverter")]
	public enum TextPosition
	{
		BottomLeft,
		BottomRight,
		Center,
		TopLeft,
		TopRight
	};

	/// <summary>
	/// Represents an interface that exposes information regarding a Text Fixed IDrawingTool.
	/// </summary>
	public class TextFixed : Text
	{
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolTextFixedTextPosition", GroupName = "NinjaScriptGeneral")]
		public TextPosition TextPosition { get; set; }

		public override void OnCalculateMinMax()
		{
			// not actually on scale, so dont participate in autoscale
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;
		}
		
		private int PaddingMultiplier(ChartControl chartControl, ChartPanel panel, bool top)
		{
			return !top
				? chartControl.ChartPanels.IndexOf(panel) == chartControl.ChartPanels.Count - 1 ? 2 : 1
				: chartControl.ChartPanels.IndexOf(panel) == 0 && chartControl.IsScrollArrowVisible ? 4 : 1;
		}

		protected override Point GetTextDrawingPosition(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale)
		{
			if (cachedTextLayout == null)
				return new Point(-1, -1);

			float x = 0;
			float y = 0;

			float textWidth 	= cachedTextLayout.Metrics.Width;
			float textHeight 	= cachedTextLayout.Metrics.Height;

			// amount of padding from panel edges
			const float padding		= 10.5f;

			// using rect BottomRight/BottomLeft etc is easiest, but we cant account for text size easily
			//Rect panelRect = new Rect(chartPanel.X, chartPanel.Y, chartPanel.W, chartPanel.H);
			//Point drawPoint;
			switch (TextPosition)
			{
				case TextPosition.BottomLeft:
					x = chartPanel.X + padding;
					y = chartPanel.Y + chartPanel.H - textHeight - padding * PaddingMultiplier(chartControl, chartPanel, false); // make enough room for copyright
					break;
				case TextPosition.BottomRight:
					x = chartPanel.X + chartPanel.W - padding - textWidth;
					y = chartPanel.Y + chartPanel.H - textHeight - padding;
					break;
				case TextPosition.Center:
					x = chartPanel.X + chartPanel.W / 2 - textWidth / 2;
					y = chartPanel.Y + chartPanel.H / 2 - textHeight / 2;
					break;
				case TextPosition.TopLeft:
					x = chartPanel.X + padding;
					y = chartPanel.Y + padding * 2; // make enough room for labels
					break;
				case TextPosition.TopRight:
					x = chartPanel.X + chartPanel.W - padding - textWidth;
					y = chartPanel.Y + (int)(padding * PaddingMultiplier(chartControl, chartPanel, true)); // make enough room for arrow indicator
					break;
			}
			// store actual layout rect we ended up with (need it for mouse points etc)
			// we need some max width here for layout to work
			return new Point(x, y);
		}

		protected override Rect GetCurrentRect(Rect layoutRect, double outlinePadding)
		{
			return new Rect(layoutRect.X - outlinePadding, layoutRect.Y - outlinePadding, layoutRect.Width + outlinePadding * 2, layoutRect.Height + outlinePadding * 2);
		}

		public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			return true;
		}

		protected override void OnStateChange()
		{
			base.OnStateChange();
			if (State == State.SetDefaults)
			{
				Name					= Custom.Resource.NinjaScriptDrawingToolTextFixed;
				// we dont need to show anchor for fixed text, it's irrelevant
				Anchor.IsBrowsable		= false;
				// always draw this last 
				ZOrderType				= DrawingToolZOrder.AlwaysDrawnLast;
				// don't let user try to select fixed text
				IgnoresUserInput		= true;
				DisplayOnChartsMenus	= false;
			}
		}
	}

	public static partial class Draw
	{
		private static Text TextCore(NinjaScriptBase owner, string tag, bool autoScale, string text,
			int barsAgo, DateTime time, double y, int? yPixelOffset, Brush textBrush, TextAlignment? textAlignment,
			Gui.Tools.SimpleFont font, Brush outlineBrush, Brush areaBrush, int? areaOpacity, bool isGlobal, string templateName,
			DashStyleHelper outlineDashStyle, int outlineWidth)
		{
			if (barsAgo == int.MinValue && time == Core.Globals.MinDate)
				throw new ArgumentException("Text: Bad barsAgo/time parameters");

			if (string.IsNullOrWhiteSpace(tag))
				throw new ArgumentException(@"tag cant be null or empty", "tag");

			if (isGlobal && tag[0] != GlobalDrawingToolManager.GlobalDrawingToolTagPrefix)
				tag = GlobalDrawingToolManager.GlobalDrawingToolTagPrefix + tag;

			Text txt = DrawingTool.GetByTagOrNew(owner, typeof(Text), tag, templateName) as Text;
			if (txt == null)
				return null;

			DrawingTool.SetDrawingToolCommonValues(txt, tag, autoScale, owner, isGlobal);

			ChartAnchor anchor	= DrawingTool.CreateChartAnchor(owner, barsAgo, time, y);

			anchor.CopyDataValues(txt.Anchor);

			// set defaults, then apply ns properties so they dont get trampled
			txt.SetState(State.Active);

			txt.DisplayText = text;
			
			if (textBrush != null)
				txt.TextBrush = textBrush;
			
			txt.UseChartTextBrush = txt.TextBrush == null;

			if (textAlignment != null)
				txt.Alignment = textAlignment.Value;
			else if(string.IsNullOrEmpty(templateName))
				txt.Alignment = TextAlignment.Center;

			if (outlineBrush != null)
				txt.OutlineStroke = new Stroke(outlineBrush, outlineDashStyle, outlineWidth) { RenderTarget = txt.OutlineStroke.RenderTarget };

			if (areaBrush != null)
				txt.AreaBrush = areaBrush;

			if (areaOpacity != null)
				txt.AreaOpacity = areaOpacity.Value;

			if (font != null)
				txt.Font = font.Clone() as Gui.Tools.SimpleFont;

			if (yPixelOffset != null)
				txt.YPixelOffset = yPixelOffset.Value;
			
			txt.ManuallyDrawn = false;

			return txt;
		}

		/// <summary>
		/// Draws text.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="text">The text you wish to draw</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <returns></returns>
		public static Text Text(NinjaScriptBase owner, string tag, string text, int barsAgo, double y)
		{
			return TextCore(owner, tag, false, text, barsAgo, Core.Globals.MinDate, y, null, null, TextAlignment.Center, null, null, null, null, false, null, DashStyleHelper.Solid, 0);
		}

		/// <summary>
		/// Draws text.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="text">The text you wish to draw</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="textBrush">The brush used to color the text of the draw object</param>
		/// <returns></returns>
		public static Text Text(NinjaScriptBase owner, string tag, string text, int barsAgo, double y, Brush textBrush)
		{
			return TextCore(owner, tag, false, text, barsAgo, Core.Globals.MinDate, y, null, textBrush, TextAlignment.Center, null, null, null, null, false, null, DashStyleHelper.Solid, 0);
		}

		/// <summary>
		/// Draws text.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="text">The text you wish to draw</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static Text Text(NinjaScriptBase owner, string tag, string text, int barsAgo, double y, bool isGlobal, string templateName)
		{
			return TextCore(owner, tag, false, text, barsAgo, Core.Globals.MinDate, y, null, null, null, null, null, null, null, isGlobal, templateName, DashStyleHelper.Solid, 0);
		}

		/// <summary>
		/// Draws text.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="text">The text you wish to draw</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="yPixelOffset">The offset value in pixels from within the text box area</param>
		/// <param name="textBrush">The brush used to color the text of the draw object</param>
		/// <param name="font">A SimpleFont object</param>
		/// <param name="alignment">The TextAlignment for the textbox</param>
		/// <param name="outlineBrush">The brush used to color the region outline of draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		public static Text Text(NinjaScriptBase owner, string tag, bool isAutoScale, string text, int barsAgo, double y, int yPixelOffset,
			Brush textBrush, Gui.Tools.SimpleFont font, TextAlignment alignment, Brush outlineBrush, Brush areaBrush, int areaOpacity)
		{
			return TextCore(owner, tag, isAutoScale, text, barsAgo, Core.Globals.MinDate, y, yPixelOffset, textBrush, alignment, font, outlineBrush, areaBrush, areaOpacity, false, null, DashStyleHelper.Solid, 2);
		}

		/// <summary>
		/// Draws text.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="text">The text you wish to draw</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="yPixelOffset">The offset value in pixels from within the text box area</param>
		/// <param name="textBrush">The brush used to color the text of the draw object</param>
		/// <param name="font">A SimpleFont object</param>
		/// <param name="alignment">The TextAlignment for the textbox</param>
		/// <param name="outlineBrush">The brush used to color the region outline of draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		public static Text Text(NinjaScriptBase owner, string tag, bool isAutoScale, string text, DateTime time, double y, int yPixelOffset,
			Brush textBrush, Gui.Tools.SimpleFont font, TextAlignment alignment, Brush outlineBrush, Brush areaBrush, int areaOpacity)
		{
			return TextCore(owner, tag, isAutoScale, text, int.MinValue, time, y, yPixelOffset, textBrush, alignment, font, outlineBrush, areaBrush, areaOpacity, false,
				null, DashStyleHelper.Solid, 2);
		}

		/// <summary>
		/// Draws text.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="text">The text you wish to draw</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="yPixelOffset">The offset value in pixels from within the text box area</param>
		/// <param name="textBrush">The brush used to color the text of the draw object</param>
		/// <param name="font">A SimpleFont object</param>
		/// <param name="alignment">The TextAlignment for the textbox</param>
		/// <param name="outlineBrush">The brush used to color the region outline of draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <param name="outlineDashStyle">The outline dash style.</param>
		/// <param name="outlineWidth">Width of the outline.</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static Text Text(NinjaScriptBase owner, string tag, bool isAutoScale, string text, int barsAgo, double y, int yPixelOffset, Brush textBrush, Gui.Tools.SimpleFont font,
			TextAlignment alignment, Brush outlineBrush, Brush areaBrush, int areaOpacity, DashStyleHelper outlineDashStyle, int outlineWidth, bool isGlobal, string templateName)
		{
			return TextCore(owner, tag, isAutoScale, text, barsAgo, Core.Globals.MinDate, y, yPixelOffset, textBrush, alignment, font, outlineBrush, areaBrush, areaOpacity, isGlobal,
				templateName, outlineDashStyle, outlineWidth);
		}

		/// <summary>
		/// Draws text.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="text">The text you wish to draw</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="yPixelOffset">The offset value in pixels from within the text box area</param>
		/// <param name="textBrush">The brush used to color the text of the draw object</param>
		/// <param name="font">A SimpleFont object</param>
		/// <param name="alignment">The TextAlignment for the textbox</param>
		/// <param name="outlineBrush">The brush used to color the region outline of draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <param name="outlineDashStyle">The outline dash style.</param>
		/// <param name="outlineWidth">Width of the outline.</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static Text Text(NinjaScriptBase owner, string tag, bool isAutoScale, string text, DateTime time, double y, int yPixelOffset, Brush textBrush, Gui.Tools.SimpleFont font,
			TextAlignment alignment, Brush outlineBrush, Brush areaBrush, int areaOpacity, DashStyleHelper outlineDashStyle, int outlineWidth, bool isGlobal, string templateName)
		{
			return TextCore(owner, tag, isAutoScale, text, int.MinValue, time, y, yPixelOffset, textBrush, alignment, font, outlineBrush, areaBrush, areaOpacity, isGlobal, 
				templateName, outlineDashStyle, outlineWidth);
		}



		// draw text fixed  //get rid of isOutlineVisible
		/// <summary>
		/// Texts the fixed core.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="text">The text you wish to draw</param>
		/// <param name="textPosition">The TextPosition of the text</param>
		/// <param name="textBrush">The brush used to color the text of the draw object</param>
		/// <param name="font">A SimpleFont object</param>
		/// <param name="outlineBrush">The brush used to color the region outline of draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <param name="outlineDashStyle">The outline dash style.</param>
		/// <param name="outlineWidth">Width of the outline.</param>
		/// <returns></returns>
		private static TextFixed TextFixedCore(NinjaScriptBase owner, string tag, string text,
			TextPosition textPosition, Brush textBrush, Gui.Tools.SimpleFont font, Brush outlineBrush,
			Brush areaBrush, int? areaOpacity, bool isGlobal, string templateName, DashStyleHelper outlineDashStyle, int outlineWidth)
		{
			TextFixed txtFixed = DrawingTool.GetByTagOrNew(owner, typeof(TextFixed), tag, templateName) as TextFixed;
			if (txtFixed == null)
				return null;

			DrawingTool.SetDrawingToolCommonValues(txtFixed, tag, false, owner, isGlobal);

			// set defaults, then apply ns properties so they dont get trampled
			txtFixed.SetState(State.Active);

			txtFixed.DisplayText 	= text;
			txtFixed.TextPosition 	= textPosition;
			
			if (textBrush != null)
				txtFixed.TextBrush = textBrush;
			
			txtFixed.UseChartTextBrush = txtFixed.TextBrush == null;

			if (outlineBrush != null)
				txtFixed.OutlineStroke = new Stroke(outlineBrush, outlineDashStyle, outlineWidth) { RenderTarget = txtFixed.OutlineStroke.RenderTarget };

			if (areaBrush != null)
				txtFixed.AreaBrush = areaBrush;

			if (areaOpacity != null)
				txtFixed.AreaOpacity = areaOpacity.Value;

			if (font != null)
				txtFixed.Font = font;

			return txtFixed;
		}

		/// <summary>
		/// Draws text in one of 5 available pre-defined fixed locations on panel 1 (price panel) of a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="text">The text you wish to draw</param>
		/// <param name="textPosition">The TextPosition of the text</param>
		/// <param name="textBrush">The brush used to color the text of the draw object</param>
		/// <param name="font">A SimpleFont object</param>
		/// <param name="outlineBrush">The brush used to color the region outline of draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		public static TextFixed TextFixed(NinjaScriptBase owner, string tag, string text, TextPosition textPosition, Brush textBrush,
			Gui.Tools.SimpleFont font, Brush outlineBrush, Brush areaBrush, int areaOpacity)
		{
			return TextFixedCore(owner, tag, text, textPosition, textBrush, font, outlineBrush, areaBrush, areaOpacity, false, null, DashStyleHelper.Solid, 2);
		}

		/// <summary>
		/// Draws text in one of 5 available pre-defined fixed locations on panel 1 (price panel) of a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="text">The text you wish to draw</param>
		/// <param name="textPosition">The TextPosition of the text</param>
		/// <param name="textBrush">The brush used to color the text of the draw object</param>
		/// <param name="font">A SimpleFont object</param>
		/// <param name="outlineBrush">The brush used to color the region outline of draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <param name="outlineDashStyle">The outline dash style.</param>
		/// <param name="outlineWidth">Width of the outline.</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static TextFixed TextFixed(NinjaScriptBase owner, string tag, string text, TextPosition textPosition, Brush textBrush,
			Gui.Tools.SimpleFont font, Brush outlineBrush, Brush areaBrush, int areaOpacity, DashStyleHelper outlineDashStyle, int outlineWidth, bool isGlobal, string templateName)
		{
			return TextFixedCore(owner, tag, text, textPosition, textBrush, font, outlineBrush, areaBrush, areaOpacity, isGlobal, templateName, outlineDashStyle, outlineWidth);
		}

		/// <summary>
		/// Draws text in one of 5 available pre-defined fixed locations on panel 1 (price panel) of a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="text">The text you wish to draw</param>
		/// <param name="textPosition">The TextPosition of the text</param>
		/// <returns></returns>
		public static TextFixed TextFixed(NinjaScriptBase owner, string tag, string text, TextPosition textPosition)
		{
			return TextFixedCore(owner, tag, text, textPosition, null, null, null, null, null, false, null, DashStyleHelper.Solid, 0);
		}

		/// <summary>
		/// Draws text in one of 5 available pre-defined fixed locations on panel 1 (price panel) of a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="text">The text you wish to draw</param>
		/// <param name="textPosition">The TextPosition of the text</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static TextFixed TextFixed(NinjaScriptBase owner, string tag, string text, TextPosition textPosition, bool isGlobal, string templateName)
		{
			return TextFixedCore(owner, tag, text, textPosition, null, null, null, null, null, isGlobal, templateName, DashStyleHelper.Solid, 0);
		}
	}
}