// 
// Copyright (C) 2020, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
#endregion

namespace NinjaTrader.NinjaScript.DrawingTools
{
	public abstract class FibonacciLevels : PriceLevelContainer
	{
		protected	const	int 			CursorSensitivity		= 15;
		private				int				priceLevelOpacity;
		protected			ChartAnchor 	editingAnchor;
		
		[Display(ResourceType=typeof(Custom.Resource), Name = "NinjaScriptDrawingToolFibonacciLevelsBaseAnchorLineStroke", GroupName = "NinjaScriptLines", Order = 1)]
		public Stroke 		AnchorLineStroke 	{ get; set; }

		// fib tools have a start and end at very least
		[Display(Order = 1)]
		public ChartAnchor 	StartAnchor 		{ get; set; }
		[Display(Order = 2)]
		public ChartAnchor 	EndAnchor 			{ get; set; }

		[Range(0, 100)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolPriceLevelsOpacity", GroupName = "NinjaScriptGeneral")]
		public int PriceLevelOpacity
		{
			get { return priceLevelOpacity; }
			set { priceLevelOpacity = Math.Max(0, Math.Min(100, value)); }
		}

		public override IEnumerable<ChartAnchor>	Anchors				{ get { return new[] { StartAnchor, EndAnchor }; } }
		public override bool						SupportsAlerts		{ get { return true; } }

		public override IEnumerable<AlertConditionItem> GetAlertConditionItems()
		{
			if (PriceLevels == null || PriceLevels.Count == 0)
				yield break;
			foreach (PriceLevel priceLevel in PriceLevels)
			{
				yield return new AlertConditionItem
				{
					Name					= priceLevel.Name,
					ShouldOnlyDisplayName	= true,
					// stuff our actual price level in the tag so we can easily find it in the alert callback
					Tag						= priceLevel,
				};
			}
		}
	}

	// note: when using a type converter attribute with dynamically loaded assemblies such as NinjaTrader custom,
	// you must pass typeconverter a string parameter. passing a type will fail to resolve
	/// <summary>
	/// Represents an interface that exposes information regarding a Fibonacci Circle IDrawingTool.
	/// </summary>
	[TypeConverter("NinjaTrader.NinjaScript.DrawingTools.FibonacciCircleTimeTypeConverter")]
	public class FibonacciCircle : FibonacciRetracements
	{
		public override object Icon { get { return Icons.DrawFbCircle; } }

		[Display(ResourceType=typeof(Custom.Resource), Name = "NinjaScriptDrawingToolFibonacciTimeExtensionsShowText", GroupName = "NinjaScriptGeneral")]
		public bool IsTextDisplayed { get; set; }
		
		[Display(ResourceType=typeof(Custom.Resource), Name = "NinjaScriptDrawingToolFibonacciTimeCircleDivideTimeSeparately", GroupName = "NinjaScriptGeneral" , Order = 1)]
		public bool IsTimePriceDividedSeparately { get; set; }

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;

			if (!IsVisible)
				return;

			foreach (ChartAnchor anchor in Anchors)
			{ 
				MinValue = Math.Min(MinValue, anchor.Price);
				MaxValue = Math.Max(MaxValue, anchor.Price);
			}
		}
	
		private void DrawPriceLevelText(float textX, float textY, PriceLevel priceLevel, double yVal, ChartControl chartControl)
		{
			if (!IsTextDisplayed)
				return;
			
			SimpleFont						wpfFont		= chartControl.Properties.LabelFont ?? new SimpleFont();
			SharpDX.DirectWrite.TextFormat	textFormat	= wpfFont.ToDirectWriteTextFormat();
			textFormat.TextAlignment					= SharpDX.DirectWrite.TextAlignment.Leading;
			textFormat.WordWrapping						= SharpDX.DirectWrite.WordWrapping.NoWrap;
			string							str			= GetPriceString(yVal, priceLevel);

			SharpDX.DirectWrite.TextLayout textLayout  = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, str, textFormat, 250, textFormat.FontSize);
			RenderTarget.DrawTextLayout(new SharpDX.Vector2(textX, textY), textLayout, priceLevel.Stroke.BrushDX, SharpDX.Direct2D1.DrawTextOptions.NoSnap);

			textFormat.Dispose();
			textLayout.Dispose();
		}

		private string GetPriceString(double yVal, PriceLevel priceLevel)
		{
			string							priceStr	= yVal.ToString(Core.Globals.GetTickFormatString(AttachedTo.Instrument.MasterInstrument.TickSize));
			string							str			= string.Format("{0} ({1})", (priceLevel.Value / 100).ToString("P", Core.Globals.GeneralOptions.CurrentCulture), priceStr);
			return str;
		}

		public override IEnumerable<Condition> GetValidAlertConditions()
		{
			return new[] { Condition.CrossInside, Condition.CrossOutside };
		}

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values,
													ChartControl chartControl, ChartScale chartScale)
		{
			if (DrawingState == DrawingState.Building)
				return false;

			PriceLevel priceLevel = conditionItem.Tag as PriceLevel;
			if (priceLevel == null)
				return false;

			ChartPanel chartPanel	= chartControl.ChartPanels[PanelIndex];
			// get our data ellipse for given price level alert is on
			Point anchorStartPoint 	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorEndPoint 	= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			
			double xRange 	= Math.Abs(anchorEndPoint.X - anchorStartPoint.X);
			double yRange 	= Math.Abs(anchorEndPoint.Y - anchorStartPoint.Y);
			double mainLine	= Math.Sqrt(Math.Pow(xRange, 2) + Math.Pow(yRange, 2));
		
			float levelFactor 	= (float)priceLevel.Value/100f;
			float r 			= (float)(levelFactor * mainLine);
			float xScale 		= (float)(levelFactor * xRange);
			float yScale 		= (float)(levelFactor * yRange);
			
			// NOTE: don't divide by two for the IsPointInsideEllipse check, this is the correct value we want already (center->r)
			float ellipseRadiusX = IsTimePriceDividedSeparately ? xScale : r;
			float ellipseRadiusY = IsTimePriceDividedSeparately ? yScale : r;

			Point centerPoint = StartAnchor.GetPoint(chartControl, chartPanel, chartScale); 

			Predicate<ChartAlertValue> predicate = v =>
			{
				Point	barPoint = new Point(chartControl.GetXByTime(v.Time), chartScale.GetYByValue(v.Value));
				bool	isInside = MathHelper.IsPointInsideEllipse(centerPoint, barPoint, ellipseRadiusX, ellipseRadiusY);
				return	condition == Condition.CrossInside ? isInside : !isInside;
			};
			return MathHelper.DidPredicateCross(values, predicate);
		}

		public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (DrawingState == DrawingState.Building)
				return true;

			// find the biggest ellipse level and use that for visibility bounds
			double biggestPriceLevelValue	= PriceLevels.Max(pl => pl.Value);
			ChartPanel chartPanel			= chartControl.ChartPanels[PanelIndex];
			Point anchorStartPoint 			= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorEndPoint 			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			
			double xRange 					= Math.Abs(anchorEndPoint.X - anchorStartPoint.X);
			double yRange 					= Math.Abs(anchorEndPoint.Y - anchorStartPoint.Y);
			double mainLine					= Math.Sqrt(Math.Pow(xRange, 2) + Math.Pow(yRange, 2));
		
			float levelFactor 				= (float)biggestPriceLevelValue/100f;
			float r 						= (float)(levelFactor * mainLine);
			float xScale 					= (float)(levelFactor * xRange);
			float yScale 					= (float)(levelFactor * yRange);
				
			float ellipseRadiusX			= IsTimePriceDividedSeparately ? xScale : r;
			float ellipseRadiusY			= IsTimePriceDividedSeparately ? yScale : r;

			double minX						= anchorStartPoint.X - ellipseRadiusX;
			double maxX						= anchorStartPoint.X + ellipseRadiusX;
			DateTime minTime				= chartControl.GetTimeByX((int) minX);
			DateTime maxTime				= chartControl.GetTimeByX((int) maxX);

			// if its completely scrolled out of time range its not visible
			if (maxTime < firstTimeOnChart || minTime > lastTimeOnChart)
				return false;

			float minY = (float)anchorStartPoint.Y - ellipseRadiusY;
			float maxY = (float)anchorStartPoint.Y + ellipseRadiusY;

			// dont forget: smaller y = greater price value
			double minVal = chartScale.GetValueByY(maxY);
			double maxVal = chartScale.GetValueByY(minY);

			// completely off scale
			if (maxVal < chartScale.MinValue || minVal > chartScale.MaxValue)
				return false;

			return true;
		}

		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			// nothing is drawn yet
			if (Anchors.All(a => a.IsEditing))
				return;

			ChartPanel chartPanel = chartControl.ChartPanels[PanelIndex];

			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			// get x distance of the line, this will be basis for our levels
			// unless extend left/right is also on
			Point anchorStartPoint 	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorEndPoint 	= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			
			AnchorLineStroke.RenderTarget = RenderTarget;
			
			SharpDX.Direct2D1.Brush tmpBrush = IsInHitTest ? chartControl.SelectionBrush : AnchorLineStroke.BrushDX;
			RenderTarget.DrawLine(anchorStartPoint.ToVector2(), anchorEndPoint.ToVector2(), tmpBrush, AnchorLineStroke.Width, AnchorLineStroke.StrokeStyle);
			
			// if we're doing a hit test pass, dont draw price levels at all, we dont want those to count for 
			// hit testing (match NT7)
			if (IsInHitTest || PriceLevels == null || !PriceLevels.Any())
				return;
			
			SetAllPriceLevelsRenderTarget();
			
			double xRange 	= Math.Abs(anchorEndPoint.X - anchorStartPoint.X);
			double yRange 	= Math.Abs(anchorEndPoint.Y - anchorStartPoint.Y);
			double mainLine	= Math.Sqrt(Math.Pow(xRange, 2) + Math.Pow(yRange, 2));

			SharpDX.Direct2D1.EllipseGeometry lastEllipse = null;
			
			foreach (PriceLevel priceLevel in PriceLevels.Where(pl => pl.IsVisible && pl.Stroke != null).OrderBy(pl => pl.Value))
			{
				float levelFactor 	= (float)priceLevel.Value/100f;
				float r 			= (float)(levelFactor * mainLine);
				float xScale 		= (float)(levelFactor * xRange);
				float yScale 		= (float)(levelFactor * yRange);
				
				// draw ellipse takes center point
				SharpDX.Vector2						startVec		= new SharpDX.Vector2((float)anchorStartPoint.X, (float)anchorStartPoint.Y);
				SharpDX.Direct2D1.Ellipse			ellipse			= IsTimePriceDividedSeparately ? new SharpDX.Direct2D1.Ellipse(startVec, xScale, yScale) : new SharpDX.Direct2D1.Ellipse(startVec, r, r);
				SharpDX.Direct2D1.EllipseGeometry	ellipseGeometry	= new SharpDX.Direct2D1.EllipseGeometry(Core.Globals.D2DFactory, ellipse);

				RenderTarget.DrawEllipse(ellipse, priceLevel.Stroke.BrushDX, priceLevel.Stroke.Width, priceLevel.Stroke.StrokeStyle);

				Stroke backgroundStroke = new Stroke();
				priceLevel.Stroke.CopyTo(backgroundStroke);
				backgroundStroke.Opacity = PriceLevelOpacity;

				if (lastEllipse == null)
					RenderTarget.FillEllipse(ellipse, backgroundStroke.BrushDX);
				else
				{
					SharpDX.Direct2D1.GeometryGroup concentricCircles = new SharpDX.Direct2D1.GeometryGroup(Core.Globals.D2DFactory, SharpDX.Direct2D1.FillMode.Alternate, new SharpDX.Direct2D1.Geometry[] {lastEllipse, ellipseGeometry });
					RenderTarget.FillGeometry(concentricCircles, backgroundStroke.BrushDX);
					concentricCircles.Dispose();
				}
				lastEllipse = ellipseGeometry;
				
			}
			if (lastEllipse != null && !lastEllipse.IsDisposed)
				lastEllipse.Dispose();

			foreach (PriceLevel priceLevel in PriceLevels.Where(pl => pl.IsVisible && pl.Stroke != null).OrderBy(pl => pl.Value))
			{
				SharpDX.Vector2 startVec = new SharpDX.Vector2((float)anchorStartPoint.X, (float)anchorStartPoint.Y);
				float levelFactor 	= (float)priceLevel.Value/100f;
				float r 			= (float)(levelFactor * mainLine);
				float yScale 		= (float)(levelFactor * yRange);
				float textX				= startVec.X;
				float textY;
				double yVal = StartAnchor.Price + (EndAnchor.Price - StartAnchor.Price) * levelFactor;

				if (IsTimePriceDividedSeparately)
					textY = startVec.Y + yScale;
				else
					textY = startVec.Y + r;

				DrawPriceLevelText(textX, textY, priceLevel, yVal, chartControl);
			}
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				AnchorLineStroke 				= new Stroke(Brushes.DarkGray, DashStyleHelper.Solid, 1f, 50);
				Name 							= Custom.Resource.NinjaScriptDrawingToolFibonacciCircle;
				PriceLevelOpacity				= 5;
				StartAnchor						= new ChartAnchor { IsEditing = true, DrawingTool = this };
				EndAnchor						= new ChartAnchor { IsEditing = true, DrawingTool = this };
				StartAnchor.DisplayName			= Custom.Resource.NinjaScriptDrawingToolAnchorStart;
				EndAnchor.DisplayName			= Custom.Resource.NinjaScriptDrawingToolAnchorEnd;
				IsTextDisplayed					= true;
				IsTimePriceDividedSeparately	= false;
			}
			else if (State == State.Configure)
			{
				if (PriceLevels.Count == 0)
				{
					PriceLevels.Add(new PriceLevel(38.2, 	Brushes.DodgerBlue));
					PriceLevels.Add(new PriceLevel(61.8, 	Brushes.CornflowerBlue));
					PriceLevels.Add(new PriceLevel(100, 	Brushes.SteelBlue));
					PriceLevels.Add(new PriceLevel(138.2, 	Brushes.DarkCyan));
					PriceLevels.Add(new PriceLevel(161.8, 	Brushes.SeaGreen));
				}
			}
			else if (State == State.Terminated)
				Dispose();
		}
	}

	/// <summary>
	/// Represents an interface that exposes information regarding a Fibonacci Retracements IDrawingTool.
	/// </summary>
	public class FibonacciRetracements : FibonacciLevels
	{
		public override object Icon { get { return Icons.DrawFbRetracement; } }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolFibonacciRetracementsExtendLinesRight", GroupName = "NinjaScriptLines")]
		public bool 					IsExtendedLinesRight 	{ get; set; }
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolFibonacciRetracementsExtendLinesLeft", GroupName = "NinjaScriptLines")]
		public bool 					IsExtendedLinesLeft 	{ get; set; }
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolFibonacciRetracementsTextLocation", GroupName = "NinjaScriptGeneral")]
		public TextLocation				TextLocation { get; set; }

		protected bool CheckAlertRetracementLine(Condition condition, Point lineStartPoint, Point lineEndPoint,
													ChartControl chartControl, ChartScale chartScale, ChartAlertValue[] values)
		{
			// not completely drawn yet?
			if (Anchors.Count(a => a.IsEditing) > 1)
				return false;

			if (values[0].ValueType == ChartAlertValueType.StaticTime)
			{
				int checkX = chartControl.GetXByTime(values[0].Time);
				return lineStartPoint.X >= checkX || lineEndPoint.X >= checkX;
			}

			double firstBarX	= chartControl.GetXByTime(values[0].Time);
			double firstBarY	= chartScale.GetYByValue(values[0].Value);
			Point barPoint		= new Point(firstBarX, firstBarY);

			 // bars passed our drawing tool line
			if (lineEndPoint.X < firstBarX)
				return false;	

			// bars not yet to our drawing tool line
			if (lineStartPoint.X > firstBarX)
				return false;

			// NOTE: 'left / right' is relative to if line was vertical. it can end up backwards too
			MathHelper.PointLineLocation pointLocation = MathHelper.GetPointLineLocation(lineStartPoint, lineEndPoint, barPoint);
			// for vertical things, think of a vertical line rotated 90 degrees to lay flat, where it's normal vector is 'up'
			switch (condition)
			{
				case Condition.Greater:			return pointLocation == MathHelper.PointLineLocation.LeftOrAbove;
				case Condition.GreaterEqual:	return pointLocation == MathHelper.PointLineLocation.LeftOrAbove || pointLocation == MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.Less:			return pointLocation == MathHelper.PointLineLocation.RightOrBelow;
				case Condition.LessEqual:		return pointLocation == MathHelper.PointLineLocation.RightOrBelow || pointLocation == MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.Equals:			return pointLocation == MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.NotEqual:		return pointLocation != MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.CrossAbove:
				case Condition.CrossBelow:
					Predicate<ChartAlertValue> predicate = v =>
					{
						if (v.Time == Core.Globals.MinDate)
							return false;
						double barX = chartControl.GetXByTime(v.Time);
						double barY = chartScale.GetYByValue(v.Value);
						Point stepBarPoint = new Point(barX, barY);
						// NOTE: 'left / right' is relative to if line was vertical. it can end up backwards too
						MathHelper.PointLineLocation ptLocation = MathHelper.GetPointLineLocation(lineStartPoint, lineEndPoint, stepBarPoint);
						if (condition == Condition.CrossAbove)
							return ptLocation == MathHelper.PointLineLocation.LeftOrAbove;
						return ptLocation == MathHelper.PointLineLocation.RightOrBelow;
					};
					return MathHelper.DidPredicateCross(values, predicate);
			}

			return false;
		}

		protected void DrawPriceLevelText(ChartPanel chartPanel, ChartScale chartScale, double minX, double maxX, double y, double price, PriceLevel priceLevel)
		{
			if (TextLocation == TextLocation.Off || priceLevel == null || priceLevel.Stroke == null || priceLevel.Stroke.BrushDX == null)
				return;
			
			// make a rectangle that sits right at our line, depending on text alignment settings
			SimpleFont						wpfFont		= chartPanel.ChartControl.Properties.LabelFont ?? new SimpleFont();
			SharpDX.DirectWrite.TextFormat	textFormat	= wpfFont.ToDirectWriteTextFormat();
			textFormat.TextAlignment					= SharpDX.DirectWrite.TextAlignment.Leading;
			textFormat.WordWrapping						= SharpDX.DirectWrite.WordWrapping.NoWrap;
			
			string str		= GetPriceString(price, priceLevel);

			// when using extreme alignments, give a few pixels of padding on the text so we dont end up right on the edge
			const double	edgePadding	= 2f;
			float			layoutWidth	= (float)Math.Abs(maxX - minX); // always give entire available width for layout
			// dont use max x for max text width here, that can break inside left/right when extended lines are on
			SharpDX.DirectWrite.TextLayout textLayout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, str, textFormat, layoutWidth, textFormat.FontSize);

			double drawAtX;

			if (IsExtendedLinesLeft && TextLocation == TextLocation.ExtremeLeft)
				drawAtX = chartPanel.X + edgePadding;
			else if (IsExtendedLinesRight && TextLocation == TextLocation.ExtremeRight)
				drawAtX = chartPanel.X + chartPanel.W - textLayout.Metrics.Width;
			else
			{
				if (TextLocation == TextLocation.InsideLeft || TextLocation == TextLocation.ExtremeLeft )
					drawAtX = minX - 1;
				else
					drawAtX = maxX - 1 - textLayout.Metrics.Width;
			}

			// we also move our y value up by text height so we draw label above line like NT7.
			RenderTarget.DrawTextLayout(new SharpDX.Vector2((float)drawAtX, (float)(y - textFormat.FontSize - edgePadding)),  textLayout, priceLevel.Stroke.BrushDX, SharpDX.Direct2D1.DrawTextOptions.NoSnap);

			textFormat.Dispose();
			textLayout.Dispose();
		}
		
		public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:	return Cursors.Pen;
				case DrawingState.Moving:	return IsLocked ? Cursors.No : Cursors.SizeAll;
				case DrawingState.Editing:
					if (IsLocked)
						return Cursors.No;
					return editingAnchor == StartAnchor ? Cursors.SizeNESW : Cursors.SizeNWSE;
				default:
					// draw move cursor if cursor is near line path anywhere
					Point startAnchorPixelPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);

					ChartAnchor closest = GetClosestAnchor(chartControl, chartPanel, chartScale, CursorSensitivity, point);
					if (closest != null)
					{
						if (IsLocked)
							return null;
						return closest == StartAnchor ? Cursors.SizeNESW : Cursors.SizeNWSE;
					}

					Vector	totalVector	= EndAnchor.GetPoint(chartControl, chartPanel, chartScale) - startAnchorPixelPoint;
					return MathHelper.IsPointAlongVector(point, startAnchorPixelPoint, totalVector, CursorSensitivity) ? 
						IsLocked ? Cursors.Arrow : Cursors.SizeAll :
						null;
			}
		}

		// Item1 = leftmost point, Item2 rightmost point of line
		protected Tuple<Point, Point> GetPriceLevelLinePoints(PriceLevel priceLevel, ChartControl chartControl, ChartScale chartScale, bool isInverted)
		{
			ChartPanel chartPanel	= chartControl.ChartPanels[PanelIndex];
			Point anchorStartPoint 	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorEndPoint 	= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			double totalPriceRange 	= EndAnchor.Price - StartAnchor.Price;
			// dont forget user could start/end draw backwards
			double anchorMinX 		= Math.Min(anchorStartPoint.X, anchorEndPoint.X);
			double anchorMaxX 		= Math.Max(anchorStartPoint.X, anchorEndPoint.X);
			double lineStartX		= IsExtendedLinesLeft ? chartPanel.X : anchorMinX;
			double lineEndX 		= IsExtendedLinesRight ? chartPanel.X + chartPanel.W : anchorMaxX;
			double levelY			= priceLevel.GetY(chartScale, StartAnchor.Price, totalPriceRange, isInverted);
			return new Tuple<Point, Point>(new Point(lineStartX, levelY), new Point(lineEndX, levelY));
		}

		private string GetPriceString(double price, PriceLevel priceLevel)
		{
			// note, dont use MasterInstrument.FormatPrice() as it will round value to ticksize which we do not want
			string priceStr	= price.ToString(Core.Globals.GetTickFormatString(AttachedTo.Instrument.MasterInstrument.TickSize));
			double pct		= priceLevel.Value / 100d;
			string str		= string.Format("{0} ({1})", pct.ToString("P", Core.Globals.GeneralOptions.CurrentCulture), priceStr);
			return str;
		}

		public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel chartPanel = chartControl.ChartPanels[PanelIndex];
			
			Point startPoint 	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point endPoint 		= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point midPoint		= new Point((startPoint.X + endPoint.X) / 2, (startPoint.Y + endPoint.Y) / 2);
			
			return new[] { startPoint, midPoint, endPoint };
		}

		
		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values, ChartControl chartControl, ChartScale chartScale)
		{
			PriceLevel priceLevel = conditionItem.Tag as PriceLevel;
			if (priceLevel == null)
				return false;
			Tuple<Point, Point>	plp = GetPriceLevelLinePoints(priceLevel, chartControl, chartScale, true);
			return CheckAlertRetracementLine(condition, plp.Item1, plp.Item2, chartControl, chartScale, values);
		}

		public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (DrawingState == DrawingState.Building)
				return true;

			DateTime minTime = Core.Globals.MaxDate;
			DateTime maxTime = Core.Globals.MinDate;
			foreach (ChartAnchor anchor in Anchors)
			{
				if (anchor.Time < minTime)
					minTime = anchor.Time;
				if (anchor.Time > maxTime)
					maxTime = anchor.Time;
			}

			if (!IsExtendedLinesLeft && !IsExtendedLinesRight)
				return new[]{minTime,maxTime}.Any(t => t >= firstTimeOnChart && t <= lastTimeOnChart) || (minTime < firstTimeOnChart && maxTime > lastTimeOnChart);

			return true;
		}

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;

			if (!IsVisible)
				return;

			// make sure *something* is drawn yet, but dont blow up if editing just a single anchor
			if (Anchors.All(a => a.IsEditing))
				return;

			double totalPriceRange 	= EndAnchor.Price - StartAnchor.Price;
			double startPrice = StartAnchor.Price;// + yPriceOffset;
			foreach (PriceLevel priceLevel in PriceLevels.Where(pl => pl.IsVisible && pl.Stroke != null))
			{
				double levelPrice	= startPrice + (1 - priceLevel.Value/100) * totalPriceRange;
				MinValue = Math.Min(MinValue, levelPrice);
				MaxValue = Math.Max(MaxValue, levelPrice);
			}
		}

		public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:
					if (StartAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(StartAnchor);
						// give end anchor something to start with so we dont try to render it with bad values right away
						dataPoint.CopyDataValues(EndAnchor);
						StartAnchor.IsEditing = false;
					}
					else if (EndAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(EndAnchor);
						EndAnchor.IsEditing = false;
					}
					
					// is initial building done (both anchors set)
					if (!StartAnchor.IsEditing && !EndAnchor.IsEditing)
					{
						DrawingState = DrawingState.Normal;
						IsSelected = false; 
					}
					break;
				case DrawingState.Normal:
					Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
					editingAnchor = GetClosestAnchor(chartControl, chartPanel, chartScale, CursorSensitivity, point);
					if (editingAnchor != null)
					{
						editingAnchor.IsEditing = true;
						DrawingState = DrawingState.Editing;
					}
					else if (editingAnchor == null || IsLocked)
					{
						// or if they didnt click particulary close to either, move (they still clicked close to our line)
						// set it to moving even if locked so we know to change cursor
						if (GetCursor(chartControl, chartPanel, chartScale, point) != null)
							DrawingState = DrawingState.Moving;
						else
							IsSelected = false;
					}
					break;
			}
		}
		
		public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (IsLocked && DrawingState != DrawingState.Building)
				return;

			if (DrawingState == DrawingState.Building)
			{
				// start anchor will not be editing here because we start building as soon as user clicks, which
				// plops down a start anchor right away
				if (EndAnchor.IsEditing)
					dataPoint.CopyDataValues(EndAnchor);
			}
			else if (DrawingState == DrawingState.Editing && editingAnchor != null)
				dataPoint.CopyDataValues(editingAnchor);
			else if (DrawingState == DrawingState.Moving)
				foreach (ChartAnchor anchor in Anchors)
					anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
		}
		
		public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			// simply end whatever moving
			if (DrawingState == DrawingState.Editing || DrawingState == DrawingState.Moving)
				DrawingState = DrawingState.Normal;
			if (editingAnchor != null)
				editingAnchor.IsEditing = false;
			editingAnchor = null;
		}
		
		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			// nothing is drawn yet
			if (Anchors.All(a => a.IsEditing))
				return;
			
			RenderTarget.AntialiasMode			= SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			ChartPanel chartPanel				= chartControl.ChartPanels[PanelIndex];
			// get x distance of the line, this will be basis for our levels
			// unless extend left/right is also on
			Point anchorStartPoint 				= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorEndPoint 				= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			
			AnchorLineStroke.RenderTarget		= RenderTarget;
			
			SharpDX.Direct2D1.Brush tmpBrush	= IsInHitTest ? chartControl.SelectionBrush : AnchorLineStroke.BrushDX;
			RenderTarget.DrawLine(anchorStartPoint.ToVector2(), anchorEndPoint.ToVector2(), tmpBrush, AnchorLineStroke.Width, AnchorLineStroke.StrokeStyle);
			
			// if we're doing a hit test pass, dont draw price levels at all, we dont want those to count for 
			// hit testing (match NT7)
			if (IsInHitTest || PriceLevels == null || !PriceLevels.Any())
				return;
			
			SetAllPriceLevelsRenderTarget();

			Point	lastStartPoint	= new Point(0, 0);
			Stroke	lastStroke		= null;

			foreach (PriceLevel priceLevel in PriceLevels.Where(pl => pl.IsVisible && pl.Stroke != null).OrderBy(p=>p.Value))
			{
				Tuple<Point, Point>	plp = GetPriceLevelLinePoints(priceLevel, chartControl, chartScale, true);

				// align to full pixel to avoid unneeded aliasing
				double strokePixAdj =	(priceLevel.Stroke.Width % 2.0).ApproxCompare(0) == 0 ? 0.5d : 0d;
				Vector pixelAdjustVec = new Vector(strokePixAdj, strokePixAdj);
			
				RenderTarget.DrawLine((plp.Item1 + pixelAdjustVec).ToVector2(), (plp.Item2 + pixelAdjustVec).ToVector2(),
										priceLevel.Stroke.BrushDX, priceLevel.Stroke.Width, priceLevel.Stroke.StrokeStyle);

				if (lastStroke == null)
					lastStroke = new Stroke();
				else
				{
					SharpDX.RectangleF borderBox = new SharpDX.RectangleF((float)lastStartPoint.X, (float)lastStartPoint.Y,
						(float)(plp.Item2.X + strokePixAdj - lastStartPoint.X), (float)(plp.Item2.Y - lastStartPoint.Y));

					RenderTarget.FillRectangle(borderBox, lastStroke.BrushDX);
				}

				priceLevel.Stroke.CopyTo(lastStroke);
				lastStroke.Opacity	= PriceLevelOpacity;
				lastStartPoint		= plp.Item1 + pixelAdjustVec;
			}

			// Render price text after background colors have rendered so the price text is on top
			foreach (PriceLevel priceLevel in PriceLevels.Where(pl => pl.IsVisible && pl.Stroke != null))
			{
				Tuple<Point, Point> plp = GetPriceLevelLinePoints(priceLevel, chartControl, chartScale, true);
				// dont always draw the text at min/max x the line renders at, pass anchor min max
				// in case text alignment is not extreme
				float	plPixAdjust		= (priceLevel.Stroke.Width % 2.0).ApproxCompare(0) == 0 ? 0.5f : 0f;
				double	anchorMinX		= Math.Min(anchorStartPoint.X, anchorEndPoint.X);
				double	anchorMaxX		= Math.Max(anchorStartPoint.X, anchorEndPoint.X) + plPixAdjust;

				double totalPriceRange 	= EndAnchor.Price - StartAnchor.Price;
				double price			= priceLevel.GetPrice(StartAnchor.Price, totalPriceRange, true);
				DrawPriceLevelText(chartPanel, chartScale, anchorMinX, anchorMaxX, plp.Item1.Y, price, priceLevel);
			}
		}
	
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				AnchorLineStroke 			= new Stroke(Brushes.DarkGray, DashStyleHelper.Solid, 1f, 50);
				Name 						= Custom.Resource.NinjaScriptDrawingToolFibonacciRetracements;
				PriceLevelOpacity			= 5;
				StartAnchor					= new ChartAnchor { IsEditing = true, DrawingTool = this };
				EndAnchor					= new ChartAnchor { IsEditing = true, DrawingTool = this };
				StartAnchor.DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorStart;
				EndAnchor.DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorEnd;
			}
			else if (State == State.Configure)
			{
				if (PriceLevels.Count == 0)
				{
					PriceLevels.Add(new PriceLevel(0,		Brushes.DarkGray));
					PriceLevels.Add(new PriceLevel(23.6,	Brushes.DodgerBlue));	
					PriceLevels.Add(new PriceLevel(38.2,	Brushes.CornflowerBlue));
					PriceLevels.Add(new PriceLevel(50,		Brushes.SteelBlue));
					PriceLevels.Add(new PriceLevel(61.8,	Brushes.DarkCyan));
					PriceLevels.Add(new PriceLevel(76.4,	Brushes.SeaGreen));
					PriceLevels.Add(new PriceLevel(100,		Brushes.DarkGray));
				}
			}
			else if (State == State.Terminated)
				Dispose();
		}
	}

	/// <summary>
	/// Represents an interface that exposes information regarding a Fibonacci Extensions IDrawingTool.
	/// </summary>
	public class FibonacciExtensions : FibonacciRetracements 
	{
		Point anchorExtensionPoint;

		[Display(Order = 3)]
		public ChartAnchor ExtensionAnchor { get; set; }
		
		public override IEnumerable<ChartAnchor> Anchors { get { return new[] { StartAnchor, EndAnchor, ExtensionAnchor }; } }
		
		protected new Tuple<Point, Point> GetPriceLevelLinePoints(PriceLevel priceLevel, ChartControl chartControl, ChartScale chartScale, bool isInverted)
		{
			ChartPanel chartPanel		= chartControl.ChartPanels[PanelIndex];
			Point anchorStartPoint 		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorEndPoint 		= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			double totalPriceRange		= EndAnchor.Price - StartAnchor.Price;
			// dont forget user could start/end draw backwards
			double anchorMinX 		= Math.Min(anchorStartPoint.X, anchorEndPoint.X);
			double anchorMaxX 		= Math.Max(anchorStartPoint.X, anchorEndPoint.X);
			double lineStartX		= IsExtendedLinesLeft ? chartPanel.X : anchorMinX;
			double lineEndX 		= IsExtendedLinesRight ? chartPanel.X + chartPanel.W : anchorMaxX;
			double levelY			= priceLevel.GetY(chartScale, ExtensionAnchor.Price, totalPriceRange, isInverted);
			return new Tuple<Point, Point>(new Point(lineStartX, levelY), new Point(lineEndX, levelY));
		}
		
		private new void DrawPriceLevelText(ChartPanel chartPanel, ChartScale chartScale, double minX, double maxX, double y, double price, PriceLevel priceLevel)
		{
			if (TextLocation == TextLocation.Off || priceLevel == null || priceLevel.Stroke == null || priceLevel.Stroke.BrushDX == null)
				return;

			// make a rectangle that sits right at our line, depending on text alignment settings
			SimpleFont wpfFont = chartPanel.ChartControl.Properties.LabelFont ?? new SimpleFont();
			SharpDX.DirectWrite.TextFormat textFormat = wpfFont.ToDirectWriteTextFormat();
			textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
			textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;

			string str = GetPriceString(price, priceLevel, chartPanel);

			// when using extreme alignments, give a few pixels of padding on the text so we dont end up right on the edge
			const double edgePadding = 2f;
			float layoutWidth = (float)Math.Abs(maxX - minX); // always give entire available width for layout
			// dont use max x for max text width here, that can break inside left/right when extended lines are on
			SharpDX.DirectWrite.TextLayout textLayout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, str, textFormat, layoutWidth, textFormat.FontSize);

			double drawAtX = minX;

			if (IsExtendedLinesLeft && TextLocation == TextLocation.ExtremeLeft)
				drawAtX = chartPanel.X + edgePadding;
			else if (IsExtendedLinesRight && TextLocation == TextLocation.ExtremeRight)
				drawAtX = chartPanel.X + chartPanel.W - textLayout.Metrics.Width;
			else
			{
				if (TextLocation == TextLocation.InsideLeft || TextLocation == TextLocation.ExtremeLeft)
					drawAtX = minX <= maxX ? minX - 1 : maxX - 1;
				else
					drawAtX = minX > maxX ? minX - textLayout.Metrics.Width : maxX - textLayout.Metrics.Width;
			}

			// we also move our y value up by text height so we draw label above line like NT7.
			RenderTarget.DrawTextLayout(new SharpDX.Vector2((float)drawAtX, (float)(y - textFormat.FontSize - edgePadding)), textLayout, priceLevel.Stroke.BrushDX, SharpDX.Direct2D1.DrawTextOptions.NoSnap);

			textFormat.Dispose();
			textLayout.Dispose();
		}

		public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
		{
			if (DrawingState != DrawingState.Normal)
				return base.GetCursor(chartControl, chartPanel, chartScale, point);

			// draw move cursor if cursor is near line path anywhere
			Point startAnchorPixelPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);

			ChartAnchor closest = GetClosestAnchor(chartControl, chartPanel, chartScale, CursorSensitivity, point);
			if (closest != null)
			{
				// show arrow until they try to move it
				if (IsLocked)
					return Cursors.Arrow;
				return closest == StartAnchor ? Cursors.SizeNESW : Cursors.SizeNWSE;
			}

			// for extensions, we want to see if the cursor along the following lines (represented as vectors):
			// start -> end, end -> ext, ext start -> ext end
			Point	endAnchorPixelPoint			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point	extPixelPoint				= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Tuple<Point, Point> extYLinePoints	= GetTranslatedExtensionYLine(chartControl, chartScale);

			Vector startEndVec	= endAnchorPixelPoint - startAnchorPixelPoint;
			Vector endExtVec	= extPixelPoint - endAnchorPixelPoint;
			Vector extYVec		= extYLinePoints.Item2 - extYLinePoints.Item1;
			// need to have an actual point to run vector along, so glue em together here
			if (new[] {	new Tuple<Vector, Point>(startEndVec, startAnchorPixelPoint), 
						new Tuple<Vector, Point>(endExtVec, endAnchorPixelPoint), 
						new Tuple<Vector, Point>(extYVec, extYLinePoints.Item1)}
					.Any(chkTup => MathHelper.IsPointAlongVector(point, chkTup.Item2, chkTup.Item1, CursorSensitivity)))
				return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
			return null;
		}

		private Point GetEndLineMidpoint(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel chartPanel	= chartControl.ChartPanels[PanelIndex];
			Point endPoint 			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point startPoint 		= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);
			return new Point((endPoint.X + startPoint.X) / 2, (endPoint.Y + startPoint.Y) / 2);
		}

		public sealed override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			Point[] pts = base.GetSelectionPoints(chartControl, chartScale);
			if (!ExtensionAnchor.IsEditing || !EndAnchor.IsEditing)
			{
				// match NT7, show 3 points along ext based on actually drawn line
				Tuple<Point, Point> extYLine = GetTranslatedExtensionYLine(chartControl, chartScale);	
				Point midExtYPoint = extYLine.Item1 + (extYLine.Item2 - extYLine.Item1) / 2;
				Point midEndPoint = GetEndLineMidpoint(chartControl, chartScale);
				return pts.Union(new[]{extYLine.Item1, extYLine.Item2, midExtYPoint, midEndPoint}).ToArray();
			}
			return pts;
		}

		private string GetPriceString(double price, PriceLevel priceLevel, ChartPanel chartPanel)
		{
			// note, dont use MasterInstrument.FormatPrice() as it will round value to ticksize which we do not want
			string priceStr = price.ToString(Core.Globals.GetTickFormatString(AttachedTo.Instrument.MasterInstrument.TickSize));
			double pct = priceLevel.Value / 100d;
			string str = string.Format("{0} ({1})", pct.ToString("P", Core.Globals.GeneralOptions.CurrentCulture), priceStr);
			return str;
		}

		private Tuple<Point, Point> GetTranslatedExtensionYLine(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel chartPanel	= chartControl.ChartPanels[PanelIndex];
			Point extPoint 			= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point startPoint 		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			double minLevelY		= double.MaxValue;
			foreach (Tuple<Point, Point> tup in PriceLevels.Where(pl => pl.IsVisible).Select(pl => GetPriceLevelLinePoints(pl, chartControl, chartScale, false)))
			{
				Vector vecToExtension	= extPoint - startPoint;
				Point adjStartPoint		= new Point((tup.Item1 + vecToExtension).X, tup.Item1.Y);

				minLevelY = Math.Min(adjStartPoint.Y, minLevelY);
			}
			if (minLevelY.ApproxCompare(double.MaxValue) == 0 )
				return new Tuple<Point, Point>(new Point(extPoint.X, extPoint.Y), new Point(extPoint.X, extPoint.Y));
			return new Tuple<Point, Point>(new Point(extPoint.X, minLevelY), new Point(extPoint.X, anchorExtensionPoint.Y));
		}

		public override object Icon { get { return Icons.DrawFbExtensions; } }

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values, ChartControl chartControl, ChartScale chartScale)
		{
			PriceLevel priceLevel = conditionItem.Tag as PriceLevel;
			if (priceLevel == null)
				return false;
			ChartPanel chartPanel		= chartControl.ChartPanels[PanelIndex];
			Tuple<Point, Point>	plp		= GetPriceLevelLinePoints(priceLevel, chartControl, chartScale, false);
			Point anchorStartPoint 		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point extensionPoint	 	= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);
			// note these points X will be based on start/end, so move to our extension 
			Vector vecToExtension		= extensionPoint - anchorStartPoint;
			Point adjStartPoint			= plp.Item1 + vecToExtension;
			Point adjEndPoint			= plp.Item2 + vecToExtension;
			return CheckAlertRetracementLine(condition, adjStartPoint, adjEndPoint, chartControl, chartScale, values);
		}

		public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			// because we have a third anchor we need to do some extra stuff here
			
			switch (DrawingState)
			{
				case DrawingState.Building:
					if (StartAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(StartAnchor);
						// give end anchor something to start with so we dont try to render it with bad values right away
						dataPoint.CopyDataValues(EndAnchor);
						StartAnchor.IsEditing = false;
					}
					else if (EndAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(EndAnchor);
						EndAnchor.IsEditing = false;

						// give extension anchor something nearby to start with
						dataPoint.CopyDataValues(ExtensionAnchor);
					}
					else if (ExtensionAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(ExtensionAnchor);
						ExtensionAnchor.IsEditing = false;
					}
					
					// is initial building done (all anchors set)
					if (Anchors.All(a => !a.IsEditing))
					{
						DrawingState 	= DrawingState.Normal;
						IsSelected 		= false; 
					}
					break;
				case DrawingState.Normal:
					Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
					// first try base mouse down
					base.OnMouseDown(chartControl, chartPanel, chartScale, dataPoint);
					if (DrawingState != DrawingState.Normal)
						break;
					// now check if they clicked along extension fibs Y line and correctly select if so
					Tuple<Point, Point> extYLinePoints	= GetTranslatedExtensionYLine(chartControl, chartScale);
					Vector extYVec						= extYLinePoints.Item2 - extYLinePoints.Item1;
					Point pointDeviceY = new Point(point.X, ConvertToVerticalPixels(chartControl, chartPanel, point.Y));
					// need to have an actual point to run vector along, so glue em together here
					if (MathHelper.IsPointAlongVector(pointDeviceY, extYLinePoints.Item1, extYVec, CursorSensitivity))
						DrawingState = DrawingState.Moving;
					else
						IsSelected = false;

					break;
			}
		}
		
		public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (IsLocked && DrawingState != DrawingState.Building)
				return;
			
			base.OnMouseMove(chartControl, chartPanel, chartScale, dataPoint);
			
			if (DrawingState == DrawingState.Building && ExtensionAnchor.IsEditing)
				dataPoint.CopyDataValues(ExtensionAnchor);
		}
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				AnchorLineStroke 			= new Stroke(Brushes.DarkGray, DashStyleHelper.Solid, 1f, 50);
				Name 						= Custom.Resource.NinjaScriptDrawingToolFibonacciExtensions;
				PriceLevelOpacity			= 5;
				StartAnchor					= new ChartAnchor { IsEditing = true, DrawingTool = this };
				ExtensionAnchor				= new ChartAnchor { IsEditing = true, DrawingTool = this };
				EndAnchor					= new ChartAnchor { IsEditing = true, DrawingTool = this };
				StartAnchor.DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorStart;
				EndAnchor.DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorEnd;
				ExtensionAnchor.DisplayName	= Custom.Resource.NinjaScriptDrawingToolAnchorExtension;
			}
			else if (State == State.Configure)
			{
				if (PriceLevels.Count == 0)
				{
					PriceLevels.Add(new PriceLevel(0,		Brushes.DarkGray));
					PriceLevels.Add(new PriceLevel(23.6,	Brushes.DodgerBlue));
					PriceLevels.Add(new PriceLevel(38.2,	Brushes.CornflowerBlue));
					PriceLevels.Add(new PriceLevel(50,		Brushes.SteelBlue));
					PriceLevels.Add(new PriceLevel(61.8,	Brushes.DarkCyan));
					PriceLevels.Add(new PriceLevel(76.4,	Brushes.SeaGreen));
					PriceLevels.Add(new PriceLevel(100,		Brushes.DarkGray));
				}
			}
			else if (State == State.Terminated)
				Dispose();
		}
		
		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			// nothing is drawn yet
			if (Anchors.All(a => a.IsEditing)) 
				return;
			
			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			// get x distance of the line, this will be basis for our levels
			// unless extend left/right is also on
			ChartPanel chartPanel			= chartControl.ChartPanels[PanelIndex];
			Point anchorStartPoint 			= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorEndPoint 			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			
			anchorExtensionPoint			= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);
			AnchorLineStroke.RenderTarget	= RenderTarget;
			
			// align to full pixel to avoid unneeded aliasing
			double strokePixAdj			= (AnchorLineStroke.Width % 2.0).ApproxCompare(0) == 0 ? 0.5d : 0d;
			Vector pixelAdjustVec		= new Vector(strokePixAdj, strokePixAdj);

			SharpDX.Vector2 startVec	= (anchorStartPoint + pixelAdjustVec).ToVector2();
			SharpDX.Vector2 endVec		= (anchorEndPoint + pixelAdjustVec).ToVector2();
			RenderTarget.DrawLine(startVec, endVec, AnchorLineStroke.BrushDX, AnchorLineStroke.Width, AnchorLineStroke.StrokeStyle);
			
			// is second anchor set yet? check both so we correctly redraw during extension anchor editing
			if (ExtensionAnchor.IsEditing && EndAnchor.IsEditing)
				return;
			
			SharpDX.Vector2			extVector	= anchorExtensionPoint.ToVector2();
			SharpDX.Direct2D1.Brush	tmpBrush	= IsInHitTest ? chartControl.SelectionBrush : AnchorLineStroke.BrushDX;
			RenderTarget.DrawLine(endVec, extVector, tmpBrush, AnchorLineStroke.Width, AnchorLineStroke.StrokeStyle);
	
			if (PriceLevels == null || !PriceLevels.Any() || IsInHitTest)
				return;

			SetAllPriceLevelsRenderTarget();

			double minLevelY = float.MaxValue;
			double maxLevelY = float.MinValue;
			Point lastStartPoint = new Point(0, 0);
			Stroke lastStroke = null;

			int count = 0;
			foreach (PriceLevel priceLevel in PriceLevels.Where(pl => pl.IsVisible && pl.Stroke != null).OrderBy(pl => pl.Value))
			{
				Tuple<Point, Point>	plp		= GetPriceLevelLinePoints(priceLevel, chartControl, chartScale, false);
				// note these points X will be based on start/end, so move to our extension
				Vector vecToExtension		= anchorExtensionPoint - anchorStartPoint;
				Point startTranslatedToExt	= plp.Item1 + vecToExtension;
				Point endTranslatedToExt	= plp.Item2 + vecToExtension;
				
				// dont nuke extended X if extend left/right is on
				double startX 				= IsExtendedLinesLeft ? plp.Item1.X : startTranslatedToExt.X;
				double endX 				= IsExtendedLinesRight ? plp.Item2.X : endTranslatedToExt.X;
				Point adjStartPoint			= new Point(startX, plp.Item1.Y);
				Point adjEndPoint			= new Point(endX, plp.Item2.Y);

				// align to full pixel to avoid unneeded aliasing
				double plPixAdjust			=	(priceLevel.Stroke.Width % 2.0).ApproxCompare(0) == 0 ? 0.5d : 0d;
				Vector plPixAdjustVec		= new Vector(plPixAdjust, plPixAdjust);
				
				// don't hit test on the price level line & text (match NT7 here), but do keep track of the min/max y
				if (!IsInHitTest)
				{
					Point startPoint = adjStartPoint + plPixAdjustVec;
					Point endPoint = adjEndPoint + plPixAdjustVec;
					
					RenderTarget.DrawLine(startPoint.ToVector2(), endPoint.ToVector2(), 
											priceLevel.Stroke.BrushDX, priceLevel.Stroke.Width, priceLevel.Stroke.StrokeStyle);

					if (lastStroke == null)
						lastStroke = new Stroke();
					else
					{
						SharpDX.RectangleF borderBox = new SharpDX.RectangleF((float)lastStartPoint.X, (float)lastStartPoint.Y,
							(float)(endPoint.X - lastStartPoint.X), (float)(endPoint.Y - lastStartPoint.Y));

						RenderTarget.FillRectangle(borderBox, lastStroke.BrushDX);
					}
					priceLevel.Stroke.CopyTo(lastStroke);
					lastStroke.Opacity = PriceLevelOpacity;
					lastStartPoint = startPoint;
				}
				minLevelY = Math.Min(adjStartPoint.Y, minLevelY);
				maxLevelY = Math.Max(adjStartPoint.Y, maxLevelY);
				count++;
			}

			foreach (PriceLevel priceLevel in PriceLevels.Where(pl => pl.IsVisible && pl.Stroke != null).OrderBy(pl => pl.Value))
			{
				if (!IsInHitTest)
				{
					Tuple<Point, Point>	plp		= GetPriceLevelLinePoints(priceLevel, chartControl, chartScale, false);
					// note these points X will be based on start/end, so move to our extension
					Vector vecToExtension		= anchorExtensionPoint - anchorStartPoint;
					Point startTranslatedToExt	= plp.Item1 + vecToExtension;
				
					// dont nuke extended X if extend left/right is on
					double startX 				= IsExtendedLinesLeft ? plp.Item1.X : startTranslatedToExt.X;
					Point adjStartPoint			= new Point(startX, plp.Item1.Y);

					double extMinX = anchorExtensionPoint.X;
					double extMaxX = anchorExtensionPoint.X + anchorEndPoint.X - anchorStartPoint.X; // actual width of lines before extension

					double totalPriceRange	= EndAnchor.Price - StartAnchor.Price;
					double price			= priceLevel.GetPrice(ExtensionAnchor.Price, totalPriceRange, false);
					DrawPriceLevelText(chartPanel, chartScale, extMinX, extMaxX, adjStartPoint.Y, price, priceLevel);
				}
			}

			// lastly draw the left edge line  at our fib lines line NT7. dont use lines start x here, it will be left edge when
			// extend left is on which we do not want
			if (count > 0)
				RenderTarget.DrawLine(new SharpDX.Vector2(extVector.X, (float)minLevelY), new SharpDX.Vector2(extVector.X, (float)maxLevelY), AnchorLineStroke.BrushDX, AnchorLineStroke.Width, AnchorLineStroke.StrokeStyle);
		}
	}

	/// <summary>
	/// Represents an interface that exposes information regarding a Fibonacci Time Extensions IDrawingTool.
	/// </summary>
	[TypeConverter("NinjaTrader.NinjaScript.DrawingTools.FibonacciCircleTimeTypeConverter")]
	public class FibonacciTimeExtensions : FibonacciRetracements
	{
		public override object Icon { get { return Icons.DrawFbFbTimeExtensions; } }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolFibonacciTimeExtensionsShowText", GroupName = "NinjaScriptGeneral")]
		public bool IsTextDisplayed { get; set; }

		public override IEnumerable<Condition> GetValidAlertConditions()
		{
			// since we're only time based, allow greater/less than stuff on x axis
			return new[] { Condition.Less, Condition.LessEqual, Condition.Equals, Condition.Greater, Condition.GreaterEqual };
		}

		private void DrawPriceLevelText(double x, PriceLevel priceLevel, ChartPanel chartPanel)
		{
			if (!IsTextDisplayed)
				return;
			
			// make a rectangle that sits right at our line, depnding on text alignment settings
			SimpleFont						wpfFont		= chartPanel.ChartControl.Properties.LabelFont ?? new SimpleFont();
			SharpDX.DirectWrite.TextFormat	textFormat	= wpfFont.ToDirectWriteTextFormat();
			textFormat.TextAlignment					= SharpDX.DirectWrite.TextAlignment.Leading;
			textFormat.WordWrapping						= SharpDX.DirectWrite.WordWrapping.NoWrap;

			string str = (priceLevel.Value/100).ToString("P", Core.Globals.GeneralOptions.CurrentCulture);
			float maxY = (float) chartPanel.Y + chartPanel.H;
						
			SharpDX.DirectWrite.TextLayout textLayout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, str, textFormat, chartPanel.W, textFormat.FontSize);
			// we also move our y value up by text height so we draw label above line like NT7.
			// the additional - 2 is so it doesnt end up right on the line. additionally bump x slightly to pad incase against panel edge

			SharpDX.Vector2	endVec	= new SharpDX.Vector2((float)x - textLayout.Metrics.Height, maxY);

			// dont forget rotation expects radians
			SharpDX.Matrix3x2 transformMatrix = SharpDX.Matrix3x2.Rotation(MathHelper.DegreesToRadians(-90), SharpDX.Vector2.Zero) * SharpDX.Matrix3x2.Translation(endVec);
			RenderTarget.Transform = transformMatrix;

			Stroke background = new Stroke();
			priceLevel.Stroke.CopyTo(background);
			background.Opacity = 70;

			RenderTarget.DrawTextLayout(new SharpDX.Vector2(0, 0), textLayout, priceLevel.Stroke.BrushDX, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
			RenderTarget.Transform = SharpDX.Matrix3x2.Identity;
			
			textFormat.Dispose();
			textLayout.Dispose();
		}

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values,
													ChartControl chartControl, ChartScale chartScale)
		{
			PriceLevel priceLevel	= conditionItem.Tag as PriceLevel;
			if (priceLevel == null)
				return false;
			ChartPanel chartPanel	= chartControl.ChartPanels[PanelIndex];
			Point anchorStartPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorEndPoint	= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			double xRange			= Math.Abs(anchorEndPoint.X - anchorStartPoint.X);
			double levelFactor		= priceLevel.Value/100d;
			double lineX			= anchorStartPoint.X + levelFactor * xRange;
			double barX				= chartControl.GetXByTime(values[0].Time);
			// we could convert the X values to time if we really wanted, but comparing the coords directly works too.
			switch (condition)
			{
				case Condition.Less:			return lineX < barX;
				case Condition.LessEqual:		return lineX <= barX;
				case Condition.Equals:			return lineX.ApproxCompare(barX) == 0;
				case Condition.Greater:			return lineX > barX;
				case Condition.GreaterEqual:	return lineX >= barX;
			}
			return false;
		}

		public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (DrawingState == DrawingState.Building)
				return true;

			ChartPanel	chartPanel			= chartControl.ChartPanels[PanelIndex];
			Point		anchorStartPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point		anchorEndPoint		= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			
			// find the min/max time and see if we cross through chart time at all
			DateTime minLinesTime = Core.Globals.MaxDate;
			DateTime maxLinesTime = Core.Globals.MinDate;
			// note: dont absolute value the range! otherwise fibs drawn backwards will always extend right
			double xRange = anchorEndPoint.X - anchorStartPoint.X;
			foreach (PriceLevel pl in PriceLevels.Where(p => p.IsVisible))
			{
				double levelFactor	= pl.Value/100d;
				double lineX		= anchorStartPoint.X + levelFactor * xRange;
				DateTime lineTime	= chartControl.GetTimeByX((int) lineX);
				if (lineTime >= firstTimeOnChart && lineTime <= lastTimeOnChart)
					return true;

				if (lineTime < minLinesTime)
					minLinesTime = lineTime;
				if (lineTime > maxLinesTime)
					maxLinesTime = lineTime;
			}
			// check a cross through
			return minLinesTime <= firstTimeOnChart && maxLinesTime >= lastTimeOnChart;
		}

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;

			if (!IsVisible)
				return;

			// only autoscale the anchors
			foreach (ChartAnchor anchor in Anchors)
			{ 
				MinValue = Math.Min(MinValue, anchor.Price);
				MaxValue = Math.Max(MaxValue, anchor.Price);
			}
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				AnchorLineStroke			= new Stroke(Brushes.DarkGray, DashStyleHelper.Solid, 1f, 50);
				Name						= Custom.Resource.NinjaScriptDrawingToolFibonacciTimeExtensions;
				PriceLevelOpacity			= 5;
				StartAnchor					= new ChartAnchor { IsEditing = true, DrawingTool = this };
				EndAnchor					= new ChartAnchor { IsEditing = true, DrawingTool = this };
				StartAnchor.DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorStart;
				EndAnchor.DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorEnd;
				IsTextDisplayed				= true;
			}
			else if (State == State.Configure)
			{
				if (PriceLevels.Count == 0)
				{
					PriceLevels.Add(new PriceLevel(0,		Brushes.DarkGray));
					PriceLevels.Add(new PriceLevel(38.2, 	Brushes.DodgerBlue));
					PriceLevels.Add(new PriceLevel(61.8, 	Brushes.CornflowerBlue));
					PriceLevels.Add(new PriceLevel(100, 	Brushes.SteelBlue));
					PriceLevels.Add(new PriceLevel(138.2, 	Brushes.DarkCyan));
					PriceLevels.Add(new PriceLevel(161.8, 	Brushes.SeaGreen));
					PriceLevels.Add(new PriceLevel(200, 	Brushes.DarkGray));
				}
			}
			else if (State == State.Terminated)
				Dispose();
		}
		
		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			// nothing is drawn yet
			if (Anchors.All(a => a.IsEditing)) 
				return;
			
			RenderTarget.AntialiasMode		= SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			// get x distance of the line, this will be basis for our levels
			// unless extend left/right is also on
			ChartPanel chartPanel			= chartControl.ChartPanels[PanelIndex];
			Point anchorStartPoint			= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorEndPoint			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			
			AnchorLineStroke.RenderTarget	= RenderTarget;
		
			// align to full pixel to avoid unneeded aliasing
			double strokePixAdj				= (AnchorLineStroke.Width % 2.0).ApproxCompare(0) == 0 ? 0.5d : 0d;
			Vector pixelAdjustVec			= new Vector(strokePixAdj, strokePixAdj);
		
			SharpDX.Direct2D1.Brush tmpBrush = IsInHitTest ? chartControl.SelectionBrush : AnchorLineStroke.BrushDX;
			RenderTarget.DrawLine((anchorStartPoint + pixelAdjustVec).ToVector2(), (anchorEndPoint + pixelAdjustVec).ToVector2(),
				tmpBrush, AnchorLineStroke.Width, AnchorLineStroke.StrokeStyle);
			
			// if we're doing a hit test pass, dont draw price levels at all, we dont want those to count for 
			// hit testing (match NT7)
			if (IsInHitTest || PriceLevels == null || !PriceLevels.Any())
				return;
			
			SetAllPriceLevelsRenderTarget();

			Stroke lastStroke = null;
			SharpDX.Vector2 lastStartPoint = new SharpDX.Vector2(0, 0);

			double xRange = anchorEndPoint.X - anchorStartPoint.X;
			foreach (PriceLevel priceLevel in PriceLevels.Where(pl => pl.IsVisible && pl.Stroke != null).OrderBy(pl => pl.Value))
			{
				double levelFactor			= priceLevel.Value/100d;
				double lineX				= anchorStartPoint.X + levelFactor * xRange;

				// align to full pixel to avoid unneeded aliasing
				double levelPixAdjust		=	(priceLevel.Stroke.Width % 2.0).ApproxCompare(0) == 0 ? 0.5d : 0d;
				SharpDX.Vector2 startVec	= new SharpDX.Vector2((float)(lineX + levelPixAdjust), chartPanel.Y);
				SharpDX.Vector2 endVec		= new SharpDX.Vector2((float)(lineX + levelPixAdjust), chartPanel.Y + chartPanel.H);
				RenderTarget.DrawLine(startVec, endVec, priceLevel.Stroke.BrushDX, priceLevel.Stroke.Width, priceLevel.Stroke.StrokeStyle);
				if (lastStroke == null)
					lastStroke = new Stroke();
				else
				{
					SharpDX.RectangleF borderBox = new SharpDX.RectangleF(lastStartPoint.X, lastStartPoint.Y,
						endVec.X - lastStartPoint.X, endVec.Y - lastStartPoint.Y);

					RenderTarget.FillRectangle(borderBox, lastStroke.BrushDX);
				}
				lastStartPoint = startVec;
				priceLevel.Stroke.CopyTo(lastStroke);
				lastStroke.Opacity = PriceLevelOpacity;
			}

			foreach (PriceLevel priceLevel in PriceLevels.Where(pl => pl.IsVisible && pl.Stroke != null).OrderBy(pl => pl.Value))
			{
				double levelFactor			= priceLevel.Value/100d;
				double lineX				= anchorStartPoint.X + levelFactor * xRange;
				double levelPixAdjust = (priceLevel.Stroke.Width % 2.0).ApproxCompare(0) == 0 ? 0.5d : 0d;
				DrawPriceLevelText(lineX + levelPixAdjust, priceLevel, chartPanel);
			}
		}
	}

	// when creating a custom type converter for drawing tools it must inherit from DrawingToolsPropertyConverter to work correctly with chart anchors
	public class FibonacciCircleTimeTypeConverter : Gui.DrawingTools.DrawingToolPropertiesConverter
	{
		public override bool GetPropertiesSupported(ITypeDescriptorContext context)
		{
			return true;
		}

		public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object value, Attribute[] attributes)
		{
			// override the GetProperties method to return a property collection that hides extended lines & text alignment properties
			// since they do not apply to fib circle / time
			PropertyDescriptorCollection propertyDescriptorCollection	= base.GetPropertiesSupported(context) ? base.GetProperties(context, value, attributes) : TypeDescriptor.GetProperties(value, attributes);
			PropertyDescriptorCollection adjusted						= new PropertyDescriptorCollection(null);
			if (propertyDescriptorCollection != null)
				foreach (PropertyDescriptor property in propertyDescriptorCollection)
					if (property.Name != "IsExtendedLinesRight" && property.Name != "IsExtendedLinesLeft" && property.Name != "TextLocation")
						adjusted.Add(property);
			return adjusted;
		}
	}

	public static partial class Draw
	{
		private static T FibonacciCore<T>(NinjaScriptBase owner, bool isAutoScale, string tag,
			int startBarsAgo, DateTime startTime, double startY, 
			int endBarsAgo, DateTime endTime, double endY, bool isGlobal, string templateName) where T : FibonacciLevels
		{
			if (owner == null)
				throw new ArgumentException("owner");
			if (startTime == Core.Globals.MinDate && endTime == Core.Globals.MinDate && startBarsAgo == int.MinValue && endBarsAgo == int.MinValue)
				throw new ArgumentException("bad start/end date/time");

			if (string.IsNullOrWhiteSpace(tag))
				throw new ArgumentException("tag cant be null or empty");

			if (isGlobal && tag[0] != GlobalDrawingToolManager.GlobalDrawingToolTagPrefix)
				tag = string.Format("{0}{1}", GlobalDrawingToolManager.GlobalDrawingToolTagPrefix, tag);

			T fibBase = DrawingTool.GetByTagOrNew(owner, typeof(T), tag, templateName) as T;
			if (fibBase == null)
				return null;

			DrawingTool.SetDrawingToolCommonValues(fibBase, tag, isAutoScale, owner, isGlobal);

			// dont nuke existing anchor refs 
			ChartAnchor		startAnchor	= DrawingTool.CreateChartAnchor(owner, startBarsAgo, startTime, startY);
			ChartAnchor		endAnchor	= DrawingTool.CreateChartAnchor(owner, endBarsAgo, endTime, endY);

			startAnchor.CopyDataValues(fibBase.StartAnchor);
			endAnchor.CopyDataValues(fibBase.EndAnchor);
			fibBase.SetState(State.Active);
			return fibBase;
		}

		// extensions has third anchor, so provide an extra base drawing function for it
		private static FibonacciExtensions FibonacciExtensionsCore(NinjaScriptBase owner, bool isAutoScale, string tag,
			int startBarsAgo, DateTime startTime, double startY, 
			int endBarsAgo, DateTime endTime, double endY,
			int extensionBarsAgo, DateTime extensionTime, double extensionY, bool isGlobal, string templateName)
		{
			FibonacciExtensions	fibExt		= FibonacciCore<FibonacciExtensions>(owner, isAutoScale, tag, startBarsAgo, 
																					startTime, startY, endBarsAgo, endTime, endY, isGlobal, templateName);

			ChartAnchor			extAnchor	= DrawingTool.CreateChartAnchor(owner, extensionBarsAgo, extensionTime, extensionY);
			extAnchor.CopyDataValues(fibExt.ExtensionAnchor);
			return fibExt;
		}

		/// <summary>
		/// Draws a fibonacci circle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <returns></returns>
		public static FibonacciCircle FibonacciCircle(NinjaScriptBase owner, string tag, bool isAutoScale, 
			DateTime startTime, double startY, DateTime endTime, double endY)
		{
			return FibonacciCore<FibonacciCircle>(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, false, null);
		}

		/// <summary>
		/// Draws a fibonacci circle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <returns></returns>
		public static FibonacciCircle FibonacciCircle(NinjaScriptBase owner, string tag, bool isAutoScale, 
			int startBarsAgo, double startY, int endBarsAgo, double endY)
		{
			return FibonacciCore<FibonacciCircle>(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, false, null);
		}

		/// <summary>
		/// Draws a fibonacci circle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static FibonacciCircle FibonacciCircle(NinjaScriptBase owner, string tag, bool isAutoScale, 
			DateTime startTime, double startY, DateTime endTime, double endY, bool isGlobal, string templateName)
		{
			return FibonacciCore<FibonacciCircle>(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a fibonacci circle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static FibonacciCircle FibonacciCircle(NinjaScriptBase owner, string tag, bool isAutoScale, 
			int startBarsAgo, double startY, int endBarsAgo, double endY, bool isGlobal, string templateName)
		{
			return FibonacciCore<FibonacciCircle>(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo,
				Core.Globals.MinDate, endY, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a fibonacci extension.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="extensionBarsAgo">The extension bars ago.</param>
		/// <param name="extensionY">The y value of the 3rd anchor point</param>
		/// <returns></returns>
		public static FibonacciExtensions FibonacciExtensions(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, 
			double startY, int endBarsAgo, double endY, int extensionBarsAgo, double extensionY)
		{
			return FibonacciExtensionsCore(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, 
				Core.Globals.MinDate, endY, extensionBarsAgo, Core.Globals.MinDate, extensionY, false, null);
		}

		/// <summary>
		/// Draws a fibonacci extension.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="extensionTime">The time of the 3rd anchor point</param>
		/// <param name="extensionY">The y value of the 3rd anchor point</param>
		/// <returns></returns>
		public static FibonacciExtensions FibonacciExtensions(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, 
			double startY, DateTime endTime, double endY, DateTime extensionTime, double extensionY)
		{
			return FibonacciExtensionsCore(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, 
				endTime, endY, int.MinValue, extensionTime, extensionY, false, null);
		}

		/// <summary>
		/// Draws a fibonacci extension.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="extensionTime">The time of the 3rd anchor point</param>
		/// <param name="extensionY">The y value of the 3rd anchor point</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static FibonacciExtensions FibonacciExtensions(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, 
			double startY, DateTime endTime, double endY, DateTime extensionTime, double extensionY, bool isGlobal, string templateName)
		{
			return FibonacciExtensionsCore(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, 
				endTime, endY, int.MinValue, extensionTime, extensionY, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a fibonacci extension.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="extensionBarsAgo">The extension bars ago.</param>
		/// <param name="extensionY">The y value of the 3rd anchor point</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static FibonacciExtensions FibonacciExtensions(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, 
			double startY, int endBarsAgo, double endY, int extensionBarsAgo, double extensionY, bool isGlobal, string templateName)
		{
			return FibonacciExtensionsCore(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, 
				Core.Globals.MinDate, endY, extensionBarsAgo, Core.Globals.MinDate, extensionY, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a fibonacci retracement.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <returns></returns>
		public static FibonacciRetracements FibonacciRetracements(NinjaScriptBase owner, string tag, bool isAutoScale, 
			DateTime startTime, double startY, DateTime endTime, double endY)
		{
			return FibonacciCore<FibonacciRetracements>(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue,
				endTime, endY, false, null);
		}

		/// <summary>
		/// Draws a fibonacci retracement.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <returns></returns>
		public static FibonacciRetracements FibonacciRetracements(NinjaScriptBase owner, string tag, bool isAutoScale, 
			int startBarsAgo, double startY, int endBarsAgo, double endY)
		{
			return FibonacciCore<FibonacciRetracements>(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, 
				Core.Globals.MinDate, endY, false, null);
		}

		/// <summary>
		/// Draws a fibonacci retracement.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static FibonacciRetracements FibonacciRetracements(NinjaScriptBase owner, string tag, bool isAutoScale, 
			DateTime startTime, double startY, DateTime endTime, double endY, bool isGlobal, string templateName)
		{
			return FibonacciCore<FibonacciRetracements>(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, 
				endTime, endY, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a fibonacci retracement.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static FibonacciRetracements FibonacciRetracements(NinjaScriptBase owner, string tag, bool isAutoScale, 
			int startBarsAgo, double startY, int endBarsAgo, double endY, bool isGlobal, string templateName)
		{
			return FibonacciCore<FibonacciRetracements>(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, 
				Core.Globals.MinDate, endY, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a fibonacci time extension.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <returns></returns>
		public static FibonacciTimeExtensions FibonacciTimeExtensions(NinjaScriptBase owner, string tag, bool isAutoScale, 
			DateTime startTime, double startY, DateTime endTime, double endY)
		{
			return FibonacciCore<FibonacciTimeExtensions>(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, false, null);
		}

		/// <summary>
		/// Draws a fibonacci time extension.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <returns></returns>
		public static FibonacciTimeExtensions FibonacciTimeExtensions(NinjaScriptBase owner, string tag, bool isAutoScale, 
			int startBarsAgo, double startY, int endBarsAgo, double endY)
		{
			return FibonacciCore<FibonacciTimeExtensions>(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, false, null);
		}

		/// <summary>
		/// Draws a fibonacci time extension.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static FibonacciTimeExtensions FibonacciTimeExtensions(NinjaScriptBase owner, string tag, bool isAutoScale, 
			DateTime startTime, double startY, DateTime endTime, double endY, bool isGlobal, string templateName)
		{
			return FibonacciCore<FibonacciTimeExtensions>(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a fibonacci time extension.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static FibonacciTimeExtensions FibonacciTimeExtensions(NinjaScriptBase owner, string tag, bool isAutoScale, 
			int startBarsAgo, double startY, int endBarsAgo, double endY, bool isGlobal, string templateName)
		{
			return FibonacciCore<FibonacciTimeExtensions>(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo,
				Core.Globals.MinDate, endY, isGlobal, templateName);
		}
	}
}
