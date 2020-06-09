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
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.DrawingTools
{
	/// <summary>
	/// Represents an object that exposes information regarding an Andrews Pitchfork IDrawingTool.
	/// </summary>
	public class AndrewsPitchfork : PriceLevelContainer
	{
		[TypeConverter("NinjaTrader.Custom.ResourceEnumConverter")]
		public enum AndrewsPitchforkCalculationMethod
		{
			StandardPitchfork,
			Schiff,
			ModifiedSchiff
		}

		private const int		cursorSensitivity		= 15;
		private ChartAnchor		editingAnchor;

		public override IEnumerable<ChartAnchor> Anchors { get { return new[] { StartAnchor, ExtensionAnchor, EndAnchor }; } }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolAnchor", GroupName = "NinjaScriptLines", Order = 1)]
		public Stroke AnchorLineStroke		{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolAndrewsPitchforkCalculationMethod", GroupName = "NinjaScriptGeneral", Order = 4)]
		public AndrewsPitchforkCalculationMethod CalculationMethod { get; set; }

		[Display(Order = 3)]
		public ChartAnchor ExtensionAnchor	{ get; set; }

		[Display(Order = 2)]
		public ChartAnchor EndAnchor		{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolAndrewsPitchforkRetracement", GroupName = "NinjaScriptLines", Order = 2)]
		public Stroke RetracementLineStroke	{ get; set; }

		[Display(ResourceType=typeof(Custom.Resource), Name="NinjaScriptDrawingToolFibonacciTimeExtensionsShowText", GroupName="NinjaScriptGeneral")]
		public bool IsTextDisplayed			{ get; set; }

		public override object Icon { get { return Gui.Tools.Icons.DrawAndrewsPitchfork; } }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolPriceLevelsOpacity", GroupName = "NinjaScriptGeneral")]
		public int PriceLevelOpacity		{ get; set; }

		[Display(Order = 1)]
		public ChartAnchor StartAnchor		{ get; set; }

		public override bool SupportsAlerts	{ get { return true; } }

		protected void DrawPriceLevelText(double minX, double maxX, Point endPoint, PriceLevel priceLevel, ChartPanel panel)
		{
			Gui.Tools.SimpleFont			wpfFont			= panel.ChartControl.Properties.LabelFont ?? new Gui.Tools.SimpleFont();
			SharpDX.DirectWrite.TextFormat	dxTextFormat	= wpfFont.ToDirectWriteTextFormat();
			string							str				= string.Format("{0}", (priceLevel.Value / 100).ToString("P"));
			SharpDX.DirectWrite.TextLayout	textLayout		= new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, str, dxTextFormat, panel.H, dxTextFormat.FontSize);

			float	usedFontHeight	= textLayout.Metrics.Height;
			float	usedFontWidth	= textLayout.Metrics.Width;
			Point	textEndPoint	= endPoint;
			double	maxWidth		= panel.X + panel.W;
			double	maxHeight		= panel.Y + panel.H;
			double	minWidth		= panel.X;
			double	minHeight		= panel.Y;

			if (textEndPoint.Y + usedFontHeight >= maxHeight)
				textEndPoint.Y = maxHeight - usedFontHeight; // Set to bottom
			if (textEndPoint.Y < minHeight) // Set to top
				textEndPoint.Y = minHeight;

			if (textEndPoint.X + usedFontWidth >= maxWidth)
				textEndPoint.X = maxWidth - usedFontWidth; //Set to right side;	
			if (textEndPoint.X < minWidth)
				textEndPoint.X = minWidth; // Set to left side;

			RenderTarget.DrawTextLayout(new SharpDX.Vector2((float)(textEndPoint.X), (float)(textEndPoint.Y)), textLayout,
				priceLevel.Stroke.BrushDX, SharpDX.Direct2D1.DrawTextOptions.NoSnap);

			dxTextFormat.Dispose();
			textLayout.Dispose();
		}

		public override IEnumerable<AlertConditionItem> GetAlertConditionItems()
		{
			if (PriceLevels == null || PriceLevels.Count == 0)
				yield break;
			foreach (PriceLevel priceLevel in PriceLevels)
			{
				yield return new AlertConditionItem
				{
					Name = priceLevel.Name,
					ShouldOnlyDisplayName = true,
					// Use the actual price level in the tag so we can easily find it in the alert callback
					Tag = priceLevel,
				};
			}
		}

		private IEnumerable<Tuple<Point, Point>> GetAndrewsEndPoints(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel	panel					= chartControl.ChartPanels[PanelIndex];
			double		totalPriceRange			= EndAnchor.Price - ExtensionAnchor.Price;
			double		startPrice				= ExtensionAnchor.Price;
			Point		anchorExtensionPoint	= ExtensionAnchor.GetPoint(chartControl, panel, chartScale);
			Point		anchorStartPoint		= StartAnchor.GetPoint(chartControl, panel, chartScale);
			Point		anchorEndPoint			= EndAnchor.GetPoint(chartControl, panel, chartScale);
			Point		midPointExtension		= new Point((anchorExtensionPoint.X + anchorEndPoint.X) / 2, (anchorExtensionPoint.Y + anchorEndPoint.Y) / 2);

			foreach (PriceLevel pl in PriceLevels.Where(pl => pl.IsVisible))
			{
				double	levelPrice	= (startPrice + ((pl.Value / 100) * totalPriceRange));
				float	pixelY		= chartScale.GetYByValue(levelPrice);
				float	pixelX		= anchorExtensionPoint.X > anchorEndPoint.X ?
					(float)(anchorExtensionPoint.X - (Math.Abs((anchorEndPoint.X - anchorExtensionPoint.X) * (pl.Value / 100)))) :
					(float)(anchorExtensionPoint.X + ((anchorEndPoint.X - anchorExtensionPoint.X) * (pl.Value / 100)));

				Point	startPoint		= new Point(pixelX, pixelY);
				Point	endPoint		= new Point(startPoint.X + (midPointExtension.X - anchorStartPoint.X), startPoint.Y + (midPointExtension.Y - anchorStartPoint.Y));
				Point	maxLevelPoint	= GetExtendedPoint(startPoint, endPoint);
				yield return new Tuple<Point, Point>(new Point(Math.Max(maxLevelPoint.X, 1), Math.Max(maxLevelPoint.Y, 1)), startPoint);
			}
		}

		public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
		{
			if (!IsVisible)
				return null;

			switch (DrawingState)
			{
				case DrawingState.Building: return Cursors.Pen;
				case DrawingState.Moving: return IsLocked ? Cursors.No : Cursors.SizeAll;
				case DrawingState.Editing:
					if (IsLocked)
						return Cursors.No;
					return editingAnchor == StartAnchor ? Cursors.SizeNESW : Cursors.SizeNWSE;
				default:
					Point startAnchorPixelPoint = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);

					ChartAnchor closest = GetClosestAnchor(chartControl, chartPanel, chartScale, cursorSensitivity, point);
					if (closest != null)
					{ 
						if (IsLocked)
							return Cursors.Arrow;
						return closest == StartAnchor ? Cursors.SizeNESW : Cursors.SizeNWSE;
					}

					// Check the anchor lines for cursor
					Point	endAnchorPixelPoint	= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
					Point	extAnchorPixelPoint	= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);
					Point	midAnchorPixelPoint	= new Point((endAnchorPixelPoint.X + extAnchorPixelPoint.X) / 2, (endAnchorPixelPoint.Y + extAnchorPixelPoint.Y) / 2);
					Vector	startToEndVec		= endAnchorPixelPoint - startAnchorPixelPoint;
					Vector	endToExtVec			= extAnchorPixelPoint - endAnchorPixelPoint;
					Vector	startToMidVec		= midAnchorPixelPoint - startAnchorPixelPoint;

					foreach (Tuple<Point, Point> endPoint in GetAndrewsEndPoints(chartControl, chartScale))
					{
						Vector andrewVector = endPoint.Item1 - endPoint.Item2;
						if (MathHelper.IsPointAlongVector(point, endPoint.Item2, andrewVector, cursorSensitivity))
						{
							return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
						}
					}

					return MathHelper.IsPointAlongVector(point, startAnchorPixelPoint, startToEndVec, cursorSensitivity) ||
										MathHelper.IsPointAlongVector(point, endAnchorPixelPoint, endToExtVec, cursorSensitivity) ||
										MathHelper.IsPointAlongVector(point, startAnchorPixelPoint, startToMidVec, cursorSensitivity) ? 
										(IsLocked ? Cursors.Arrow : Cursors.SizeAll) : null;
			}
		}

		public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			if (!IsVisible)
				return new Point[0];

			ChartPanel chartPanel	= chartControl.ChartPanels[PanelIndex];
			Point startPoint		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point endPoint			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point midPoint			= new Point((startPoint.X + endPoint.X) / 2, (startPoint.Y + endPoint.Y) / 2);
			Point extPoint			= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);

			return new[] { startPoint, midPoint, endPoint, extPoint };
		}

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values,
													ChartControl chartControl, ChartScale chartScale)
		{
			PriceLevel priceLevel = conditionItem.Tag as PriceLevel;
			if (priceLevel == null)
				return false;

			ChartPanel chartPanel		= chartControl.ChartPanels[PanelIndex];
			Point anchorStartPoint		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorEndPoint		= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorExtensionPoint	= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point midPointExtension		= new Point((anchorExtensionPoint.X + anchorEndPoint.X) / 2, (anchorExtensionPoint.Y + anchorEndPoint.Y) / 2);

			if (CalculationMethod == AndrewsPitchforkCalculationMethod.Schiff)
				anchorStartPoint = new Point(anchorStartPoint.X, (anchorStartPoint.Y + anchorEndPoint.Y) / 2);
			else if (CalculationMethod == AndrewsPitchforkCalculationMethod.ModifiedSchiff)
				anchorStartPoint = new Point((anchorEndPoint.X + anchorStartPoint.X) / 2, (anchorEndPoint.Y + anchorStartPoint.Y) / 2);

			double totalPriceRange	= EndAnchor.Price - ExtensionAnchor.Price;
			double startPrice		= ExtensionAnchor.Price;

			double levelPrice	= (startPrice + ((priceLevel.Value / 100) * totalPriceRange));
			float pixelY		= chartScale.GetYByValue(levelPrice);
			float pixelX		= anchorExtensionPoint.X > anchorEndPoint.X ? 
				(float)(anchorExtensionPoint.X - (Math.Abs((anchorEndPoint.X - anchorExtensionPoint.X) * (priceLevel.Value / 100)))) :
				(float)(anchorExtensionPoint.X + ((anchorEndPoint.X - anchorExtensionPoint.X) * (priceLevel.Value / 100)));

			Point alertStartPoint	= new Point(pixelX, pixelY);
			Point endPoint			= new Point(alertStartPoint.X + (midPointExtension.X - anchorStartPoint.X), alertStartPoint.Y + (midPointExtension.Y - anchorStartPoint.Y));
			Point alertEndPoint		= GetExtendedPoint(alertStartPoint, endPoint);

			double firstBarX		= values[0].ValueType == ChartAlertValueType.StaticValue ? pixelX : chartControl.GetXByTime(values[0].Time);
			double firstBarY		= chartScale.GetYByValue(values[0].Value);
			Point barPoint			= new Point(firstBarX, firstBarY);

			// Check bars are not yet to our drawing tool
			if (firstBarX < alertStartPoint.X || firstBarX > alertEndPoint.X)
				return false;

			// NOTE: 'left / right' is relative to if line was vertical. It can end up backwards too
			MathHelper.PointLineLocation pointLocation = MathHelper.GetPointLineLocation(alertStartPoint, alertEndPoint, barPoint);
			// For vertical things, think of a vertical line rotated 90 degrees to lay flat, where it's normal vector is 'up'
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
						double barX = chartControl.GetXByTime(v.Time);
						double barY = chartScale.GetYByValue(v.Value);
						Point stepBarPoint = new Point(barX, barY);
						// NOTE: 'left / right' is relative to if line was vertical. It can end up backwards too
						MathHelper.PointLineLocation ptLocation = MathHelper.GetPointLineLocation(alertStartPoint, alertEndPoint, stepBarPoint);
						if (condition == Condition.CrossAbove)
							return ptLocation == MathHelper.PointLineLocation.LeftOrAbove;
						return ptLocation == MathHelper.PointLineLocation.RightOrBelow;
					};
					return MathHelper.DidPredicateCross(values, predicate);
			}

			return false;
		}

		public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			// First check any of the actual anchors are visible, we are immediately done
			foreach (ChartAnchor anchor in Anchors)
			{
				if (anchor.IsEditing || anchor.Time >= firstTimeOnChart && anchor.Time <= lastTimeOnChart)
					return true;
			}

			// Calculate extensions and see if they extend into our visible times
			ChartPanel chartPanel		= chartControl.ChartPanels[PanelIndex];
			Point anchorStartPoint		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorEndPoint		= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorExtensionPoint	= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point midPointExtension		= new Point((anchorExtensionPoint.X + anchorEndPoint.X) / 2, (anchorExtensionPoint.Y + anchorEndPoint.Y) / 2);

			if (CalculationMethod == AndrewsPitchforkCalculationMethod.Schiff)
				anchorStartPoint = new Point(anchorStartPoint.X, (anchorStartPoint.Y + anchorEndPoint.Y) / 2);
			else if (CalculationMethod == AndrewsPitchforkCalculationMethod.ModifiedSchiff)
				anchorStartPoint = new Point((anchorEndPoint.X + anchorStartPoint.X) / 2, (anchorEndPoint.Y + anchorStartPoint.Y) / 2);

			double totalPriceRange	= EndAnchor.Price - ExtensionAnchor.Price;
			double startPrice		= ExtensionAnchor.Price;

			foreach (PriceLevel priceLevel in PriceLevels.Where(pl => pl.IsVisible && pl.Stroke != null))
			{
				double levelPrice	= (startPrice + ((priceLevel.Value / 100) * totalPriceRange));
				float pixelY		= chartScale.GetYByValue(levelPrice);
				float pixelX		= anchorExtensionPoint.X > anchorEndPoint.X ?
					priceLevel.Value >= 0 ? (float)(anchorExtensionPoint.X - (Math.Abs((anchorEndPoint.X - anchorExtensionPoint.X) * (priceLevel.Value / 100)))) : (float)(anchorExtensionPoint.X + ((anchorEndPoint.X - anchorExtensionPoint.X) * (priceLevel.Value / 100))):
					priceLevel.Value >= 0 ? (float)(anchorExtensionPoint.X + ((anchorEndPoint.X - anchorExtensionPoint.X) * (priceLevel.Value / 100))) : (float)(anchorExtensionPoint.X - (Math.Abs((anchorEndPoint.X - anchorExtensionPoint.X) * (priceLevel.Value / 100))));

				Point startPoint	= new Point(pixelX, pixelY);
				Point endPoint		= new Point(startPoint.X + (midPointExtension.X - anchorStartPoint.X), startPoint.Y + (midPointExtension.Y - anchorStartPoint.Y));
				Point maxLevelPoint	= GetExtendedPoint(startPoint, endPoint);

				double padding = 5d;
				foreach (Point chkPoint in new[]{startPoint, maxLevelPoint, endPoint})
					if (chkPoint.X >= chartPanel.X - padding && chkPoint.X <= chartPanel.W + chartPanel.X + padding 
						&& chkPoint.Y >= chartPanel.Y - padding && chkPoint.Y <= chartPanel.Y + chartPanel.H + padding)
						return true;
			}
			return false;
		}

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

		public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:
					if (StartAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(StartAnchor);
						// Give end anchor something to start with so we dont try to render it with bad values right away
						dataPoint.CopyDataValues(EndAnchor);
						StartAnchor.IsEditing = false;
					}
					else if (EndAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(EndAnchor);
						// Give extension anchor something nearby to start with
						dataPoint.CopyDataValues(ExtensionAnchor);
						EndAnchor.IsEditing = false;
					}
					else if (ExtensionAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(ExtensionAnchor);
						ExtensionAnchor.IsEditing = false;
					}

					// Is initial building done (all anchors set)
					if (Anchors.All(a => !a.IsEditing))
					{
						DrawingState = DrawingState.Normal;
						IsSelected = false;
					}
					break;
				case DrawingState.Normal:
					Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
					editingAnchor = GetClosestAnchor(chartControl, chartPanel, chartScale, cursorSensitivity, point);
					if (editingAnchor != null)
					{
						editingAnchor.IsEditing = true;
						DrawingState = DrawingState.Editing;
					}
					else
					{
						if (GetCursor(chartControl, chartPanel, chartScale, point) == Cursors.SizeAll)
							DrawingState = DrawingState.Moving;
						else if (GetCursor(chartControl, chartPanel, chartScale, point) == Cursors.SizeNESW ||
							GetCursor(chartControl, chartPanel, chartScale, point) == Cursors.SizeNWSE)
							DrawingState = DrawingState.Editing;
						else if (GetCursor(chartControl, chartPanel, chartScale, point) == Cursors.Arrow)
							DrawingState = DrawingState.Editing;
						else if (GetCursor(chartControl, chartPanel, chartScale, point) == null)
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
				// Start anchor will not be editing here because we start building as soon as user clicks, which
				// plops down a start anchor right away
				if (EndAnchor.IsEditing)
					dataPoint.CopyDataValues(EndAnchor);
				else if (ExtensionAnchor.IsEditing)
					dataPoint.CopyDataValues(ExtensionAnchor);
			}
			else if (DrawingState == DrawingState.Editing && editingAnchor != null)
				dataPoint.CopyDataValues(editingAnchor);
			else if (DrawingState == DrawingState.Moving)
			{
				foreach (ChartAnchor anchor in Anchors)
					anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
				// Don't forget to update delta to last used
			}
		}

		public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (DrawingState == DrawingState.Editing || DrawingState == DrawingState.Moving)
				DrawingState = DrawingState.Normal;
			if (editingAnchor != null)
				editingAnchor.IsEditing = false;
			editingAnchor = null;
		}

		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (Anchors.All(a => a.IsEditing))
				return;

			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

			ChartPanel chartPanel		= chartControl.ChartPanels[PanelIndex];
			Point anchorStartPoint		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorEndPoint		= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorExtensionPoint	= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point midPointExtension		= new Point((anchorExtensionPoint.X + anchorEndPoint.X) / 2, (anchorExtensionPoint.Y + anchorEndPoint.Y) / 2);

			if (CalculationMethod == AndrewsPitchforkCalculationMethod.Schiff)
				anchorStartPoint = new Point(anchorStartPoint.X, (anchorStartPoint.Y + anchorEndPoint.Y) / 2);
			else if (CalculationMethod == AndrewsPitchforkCalculationMethod.ModifiedSchiff)
				anchorStartPoint = new Point((anchorEndPoint.X + anchorStartPoint.X) / 2, (anchorEndPoint.Y + anchorStartPoint.Y) / 2);

			AnchorLineStroke.RenderTarget			= RenderTarget;
			RetracementLineStroke.RenderTarget		= RenderTarget;

			// Align to full pixel to avoid unneeded aliasing
			double					strokePixAdj	= AnchorLineStroke.Width % 2 == 0 ? 0.5d : 0d;
			Vector					pixelAdjustVec	= new Vector(strokePixAdj, strokePixAdj);
			SharpDX.Vector2			startVec		= (anchorStartPoint + pixelAdjustVec).ToVector2();
			SharpDX.Vector2			endVec			= (anchorEndPoint + pixelAdjustVec).ToVector2();
			SharpDX.Direct2D1.Brush	tmpBrush		= IsInHitTest ? chartControl.SelectionBrush : AnchorLineStroke.BrushDX;

			SharpDX.Vector2			startOriginVec	= (StartAnchor.GetPoint(chartControl, chartPanel, chartScale) + pixelAdjustVec).ToVector2();

			RenderTarget.DrawLine(startOriginVec, endVec, tmpBrush, AnchorLineStroke.Width, AnchorLineStroke.StrokeStyle);

			// Is second anchor set yet? Check both so we correctly re-draw during extension anchor editing
			if (ExtensionAnchor.IsEditing && EndAnchor.IsEditing)
				return;

			SharpDX.Vector2 extVector	= anchorExtensionPoint.ToVector2();
			tmpBrush					= IsInHitTest ? chartControl.SelectionBrush : RetracementLineStroke.BrushDX;
			RenderTarget.DrawLine(endVec, extVector, tmpBrush, RetracementLineStroke.Width, RetracementLineStroke.StrokeStyle);

			// If we're doing a hit test pass, don't draw price levels at all, we dont want those to count for 
			// hit testing
			if (IsInHitTest || PriceLevels == null || !PriceLevels.Any())
				return;

			SetAllPriceLevelsRenderTarget();

			// Calculate total y range for % calculation on each level
			double totalPriceRange	= EndAnchor.Price - ExtensionAnchor.Price;
			double startPrice		= ExtensionAnchor.Price;
			float minLevelY			= float.MaxValue;
			float maxLevelY			= float.MinValue;

			// Store values to use in correct render order
			Point lastEndPoint		= new Point(0, 0);
			Point lastStartPoint	= new Point(0, 0);
			Stroke lastStroke		= null;
			List<Tuple<PriceLevel, Point>> textPoints = new List<Tuple<PriceLevel, Point>>();

			foreach (PriceLevel priceLevel in PriceLevels.Where(pl => pl.IsVisible && pl.Stroke != null).OrderBy(pl=>pl.Value))
			{
				double levelPrice	= (startPrice + ((priceLevel.Value / 100) * totalPriceRange));
				float pixelY		= chartScale.GetYByValue(levelPrice);
				float pixelX		= anchorExtensionPoint.X > anchorEndPoint.X ?
					priceLevel.Value >= 0 ? (float)(anchorExtensionPoint.X - (Math.Abs((anchorEndPoint.X - anchorExtensionPoint.X) * (priceLevel.Value / 100)))) : (float)(anchorExtensionPoint.X + ((anchorEndPoint.X - anchorExtensionPoint.X) * (priceLevel.Value / 100))):
					priceLevel.Value >= 0 ? (float)(anchorExtensionPoint.X + ((anchorEndPoint.X - anchorExtensionPoint.X) * (priceLevel.Value / 100))) : (float)(anchorExtensionPoint.X - (Math.Abs((anchorEndPoint.X - anchorExtensionPoint.X) * (priceLevel.Value / 100))));
				Point startPoint	= new Point(pixelX, pixelY);
				Point endPoint		= new Point(startPoint.X + (midPointExtension.X - anchorStartPoint.X), startPoint.Y + (midPointExtension.Y - anchorStartPoint.Y));
				Point maxLevelPoint	= GetExtendedPoint(startPoint, endPoint);
				if (priceLevel.Value == 50)
					RenderTarget.DrawLine(startVec, maxLevelPoint.ToVector2(), priceLevel.Stroke.BrushDX, priceLevel.Stroke.Width, priceLevel.Stroke.StrokeStyle);
				else
					RenderTarget.DrawLine(startPoint.ToVector2(), maxLevelPoint.ToVector2(), priceLevel.Stroke.BrushDX, priceLevel.Stroke.Width, priceLevel.Stroke.StrokeStyle);

				if (lastStroke == null)
					lastStroke = new Stroke();
				else
				{
					SharpDX.Direct2D1.PathGeometry lineGeometry = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
					SharpDX.Direct2D1.GeometrySink sink = lineGeometry.Open();
					sink.BeginFigure(lastEndPoint.ToVector2(), SharpDX.Direct2D1.FigureBegin.Filled);
					// Does the fill color need to fill a corner?  Check and add a point
					if (lastEndPoint.Y != maxLevelPoint.Y && lastEndPoint.X != maxLevelPoint.X)
					{
						double boundaryX;
						double boundaryY;

						if (lastEndPoint.Y <= ChartPanel.Y || lastEndPoint.Y >= ChartPanel.Y + ChartPanel.H)
						{
							boundaryY = lastEndPoint.Y;
							boundaryX = maxLevelPoint.X;
						}
						else
						{
							boundaryY = maxLevelPoint.Y;
							boundaryX = lastEndPoint.X;
						}
						sink.AddLine(new SharpDX.Vector2((float)boundaryX, (float)boundaryY));
					}
					sink.AddLine(maxLevelPoint.ToVector2());
					sink.AddLine(startPoint.ToVector2());
					sink.AddLine(lastStartPoint.ToVector2());
					sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
					sink.Close();
					RenderTarget.FillGeometry(lineGeometry, lastStroke.BrushDX);
					lineGeometry.Dispose();
				}

				if (IsTextDisplayed)
					textPoints.Add(new Tuple<PriceLevel, Point>(priceLevel, maxLevelPoint));

				priceLevel.Stroke.CopyTo(lastStroke);
				lastStroke.Opacity	= PriceLevelOpacity;
				lastStartPoint		= startPoint;
				lastEndPoint		= maxLevelPoint;
				minLevelY			= Math.Min(pixelY, minLevelY);
				maxLevelY			= Math.Max(pixelY, maxLevelY);
			}

			// Render text last so it's on top of the price level colors
			if (IsTextDisplayed)
				foreach (Tuple<PriceLevel, Point> textPoint in textPoints)
					DrawPriceLevelText(0, 0, textPoint.Item2, textPoint.Item1, chartPanel);
		}

		protected override void OnStateChange()
		{
			base.OnStateChange();
			switch(State)
			{
				case State.SetDefaults:
					AnchorLineStroke			= new Stroke(Brushes.DarkGray, DashStyleHelper.Solid, 1f, 50);
					RetracementLineStroke		= new Stroke(Brushes.SeaGreen, DashStyleHelper.Solid, 2f);
					Description					= Custom.Resource.NinjaScriptDrawingToolAndrewsPitchforkDescription;
					Name						= Custom.Resource.NinjaScriptDrawingToolAndrewsPitchfork;
					StartAnchor					= new ChartAnchor { IsEditing = true, DrawingTool = this };
					ExtensionAnchor				= new ChartAnchor { IsEditing = true, DrawingTool = this };
					EndAnchor					= new ChartAnchor { IsEditing = true, DrawingTool = this };
					StartAnchor.DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorStart;
					EndAnchor.DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorEnd;
					ExtensionAnchor.DisplayName	= Custom.Resource.NinjaScriptDrawingToolAnchorExtension;
					PriceLevelOpacity			= 5;
					IsTextDisplayed				= true;
					break;
				case State.Configure:
					if (PriceLevels.Count == 0)
					{
						PriceLevels.Add(new PriceLevel(0,	Brushes.SeaGreen));
						PriceLevels.Add(new PriceLevel(50,	Brushes.SeaGreen));
						PriceLevels.Add(new PriceLevel(100,	Brushes.SeaGreen));
					}
					break;
				case State.Terminated:
					Dispose();
					break;
			}
		}
	}

	public static partial class Draw
	{
		private static AndrewsPitchfork AndrewsPitchforkCore(NinjaScriptBase owner,
			string tag, bool isAutoScale, 
			int anchor1BarsAgo, DateTime anchor1Time, double anchor1Y,
			int anchor2BarsAgo, DateTime anchor2Time, double anchor2Y,
			int anchor3BarsAgo, DateTime anchor3Time, double anchor3Y,
			Brush brush, DashStyleHelper dashStyle, int width,
			bool isGlobal, string templateName)
		{
			if (owner == null)
				throw new ArgumentException("owner");

			if (string.IsNullOrWhiteSpace(tag))
				throw new ArgumentException(@"tag cant be null or empty", "tag");

			if (isGlobal && tag[0] != GlobalDrawingToolManager.GlobalDrawingToolTagPrefix)
				tag = GlobalDrawingToolManager.GlobalDrawingToolTagPrefix + tag;

			AndrewsPitchfork pitchfork = DrawingTool.GetByTagOrNew(owner, typeof(AndrewsPitchfork), tag, templateName) as AndrewsPitchfork;
			if (pitchfork == null)
				return null;

			DrawingTool.SetDrawingToolCommonValues(pitchfork, tag, isAutoScale, owner, isGlobal);

			ChartAnchor startAnchor	= DrawingTool.CreateChartAnchor(owner, anchor1BarsAgo, anchor1Time, anchor1Y);
			ChartAnchor endAnchor	= DrawingTool.CreateChartAnchor(owner, anchor2BarsAgo, anchor2Time, anchor2Y);
			ChartAnchor extAnchor	= DrawingTool.CreateChartAnchor(owner, anchor3BarsAgo, anchor3Time, anchor3Y);

			startAnchor.CopyDataValues(pitchfork.StartAnchor);
			endAnchor.CopyDataValues(pitchfork.EndAnchor);
			extAnchor.CopyDataValues(pitchfork.ExtensionAnchor);

			if (string.IsNullOrEmpty(templateName) || brush != null)
			{
				pitchfork.AnchorLineStroke.Width	= width;
				pitchfork.RetracementLineStroke		= new Stroke(brush, dashStyle, width) { RenderTarget = pitchfork.RetracementLineStroke.RenderTarget };
			}

			pitchfork.SetState(State.Active);
			return pitchfork;
		}

		/// <summary>
		/// Draws an Andrew's Pitchfork.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="anchor1BarsAgo">The number of bars ago (x value) of the 1st anchor point</param>
		/// <param name="anchor1Y">The y value coordinate of the first anchor point</param>
		/// <param name="anchor2BarsAgo">The number of bars ago (x axis coordinate) to draw the second anchor point</param>
		/// <param name="anchor2Y">The y value coordinate of the second anchor point</param>
		/// <param name="anchor3BarsAgo">The number of bars ago (x axis coordinate) to draw the third anchor point</param>
		/// <param name="anchor3Y">The y value coordinate of the third anchor point</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <returns></returns>
		public static AndrewsPitchfork AndrewsPitchfork(NinjaScriptBase owner, string tag, bool isAutoScale,
				int anchor1BarsAgo, double anchor1Y, 
				int anchor2BarsAgo, double anchor2Y, 
				int anchor3BarsAgo, double anchor3Y, 
				Brush brush, DashStyleHelper dashStyle, int width)
		{
			return AndrewsPitchforkCore(owner, tag, isAutoScale, 
				anchor1BarsAgo, Core.Globals.MinDate, anchor1Y,
				anchor2BarsAgo, Core.Globals.MinDate, anchor2Y,
				anchor3BarsAgo, Core.Globals.MinDate, anchor3Y, brush, dashStyle, width, false, null);
		}

		/// <summary>
		/// Draws an Andrew's Pitchfork.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="anchor1Time">The time at which to draw the first anchor point</param>
		/// <param name="anchor1Y">The y value coordinate of the first anchor point</param>
		/// <param name="anchor2Time">The time at which to draw the second anchor point</param>
		/// <param name="anchor2Y">The y value coordinate of the second anchor point</param>
		/// <param name="anchor3Time">The time at which to draw the third anchor point</param>
		/// <param name="anchor3Y">The y value coordinate of the third anchor point</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object</param>
		/// <param name="width">The width of the draw object</param>
		/// <returns></returns>
		public static AndrewsPitchfork AndrewsPitchfork(NinjaScriptBase owner, string tag, bool isAutoScale,
				DateTime anchor1Time, double anchor1Y, 
				DateTime anchor2Time, double anchor2Y, 
				DateTime anchor3Time, double anchor3Y, 
				Brush brush, DashStyleHelper dashStyle, int width)
		{
			return AndrewsPitchforkCore(owner, tag, isAutoScale, 
				int.MinValue, anchor1Time, anchor1Y,
				int.MinValue, anchor2Time, anchor2Y,
				int.MinValue, anchor3Time, anchor3Y, brush, dashStyle, width, false, null);
		}

		/// <summary>
		/// Draws an Andrew's Pitchfork.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="anchor1BarsAgo">The number of bars ago (x value) of the 1st anchor point</param>
		/// <param name="anchor1Y">The y value coordinate of the first anchor point</param>
		/// <param name="anchor2BarsAgo">The number of bars ago (x axis coordinate) to draw the second anchor point</param>
		/// <param name="anchor2Y">The y value coordinate of the second anchor point</param>
		/// <param name="anchor3BarsAgo">The number of bars ago (x axis coordinate) to draw the third anchor point</param>
		/// <param name="anchor3Y">The y value coordinate of the third anchor point</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static AndrewsPitchfork AndrewsPitchfork(NinjaScriptBase owner, string tag, bool isAutoScale,
				int anchor1BarsAgo, double anchor1Y, 
				int anchor2BarsAgo, double anchor2Y, 
				int anchor3BarsAgo, double anchor3Y, 
				bool isGlobal, string templateName)
		{
			return AndrewsPitchforkCore(owner, tag, isAutoScale, 
				anchor1BarsAgo, Core.Globals.MinDate, anchor1Y,
				anchor2BarsAgo, Core.Globals.MinDate, anchor2Y,
				anchor3BarsAgo, Core.Globals.MinDate, anchor3Y, null, DashStyleHelper.Solid, 0, isGlobal, templateName);
		}

		/// <summary>
		/// Draws an Andrew's Pitchfork.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="anchor1Time">The time at which to draw the first anchor point</param>
		/// <param name="anchor1Y">The y value coordinate of the first anchor point</param>
		/// <param name="anchor2Time">The time at which to draw the second anchor point</param>
		/// <param name="anchor2Y">The y value coordinate of the second anchor point</param>
		/// <param name="anchor3Time">The time at which to draw the third anchor point</param>
		/// <param name="anchor3Y">The y value coordinate of the third anchor point</param>
		/// <param name="isGlobal">if set to <c>true</c> [is global].</param>
		/// <param name="templateName">Name of the template.</param>
		/// <returns></returns>
		public static AndrewsPitchfork AndrewsPitchfork(NinjaScriptBase owner, string tag, bool isAutoScale,
				DateTime anchor1Time, double anchor1Y, 
				DateTime anchor2Time, double anchor2Y, 
				DateTime anchor3Time, double anchor3Y, 
				bool isGlobal, string templateName)
		{
			return AndrewsPitchforkCore(owner, tag, isAutoScale, 
				int.MinValue, anchor1Time, anchor1Y,
				int.MinValue, anchor2Time, anchor2Y,
				int.MinValue, anchor3Time, anchor3Y, null, DashStyleHelper.Solid, 0, isGlobal, templateName);
		}
	}
}