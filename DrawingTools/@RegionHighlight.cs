// 
// Copyright (C) 2021, NinjaTrader LLC <www.ninjatrader.com>.
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
using System.Xml.Serialization;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;

#endregion

namespace NinjaTrader.NinjaScript.DrawingTools
{
	internal enum RegionHighlightMode
	{
		Time,	// x
		Price,	// y
	}

	[CLSCompliant(false)]
	public abstract class RegionHighlightBase : DrawingTool
	{
		private				int							areaOpacity;
		private				Brush						areaBrush;
		private	readonly	DeviceBrush					areaBrushDevice			= new DeviceBrush();
		private	const		double						cursorSensitivity		= 15;
		private				ChartAnchor 				editingAnchor;
		private				bool						hasSetZOrder;

		public override bool SupportsAlerts { get { return true; } }

		public override IEnumerable<ChartAnchor> Anchors
		{
			get { return new[] { StartAnchor, EndAnchor }; }
		}

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolRiskRewardAnchorLineStroke", GroupName = "NinjaScriptGeneral", Order = 5)]
		public Stroke AnchorLineStroke { get; set; }

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolShapesAreaBrush", GroupName = "NinjaScriptGeneral", Order = 3)]
		public Brush AreaBrush
		{
			get { return areaBrush; }
			set
			{
				areaBrush = value;
				if (areaBrush != null)
				{
					if (areaBrush.IsFrozen)
						areaBrush = areaBrush.Clone();
					areaBrush.Opacity = areaOpacity / 100d;
					areaBrush.Freeze();
				}
				areaBrushDevice.Brush = null;
			}
		}

		[Browsable(false)]
		public string AreaBrushSerialize
		{
			get { return Serialize.BrushToString(AreaBrush); }
			set { AreaBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolAreaOpacity", GroupName = "NinjaScriptGeneral", Order = 4)]
		public int AreaOpacity
		{
			get { return areaOpacity; }
			set
			{
				areaOpacity = Math.Max(0, Math.Min(100, value));
				areaBrushDevice.Brush = null;
			}
		}

		[Display(Order = 2)]
		public ChartAnchor EndAnchor { get; set; }

		[Browsable(false)]
		[XmlIgnore]
		internal RegionHighlightMode Mode { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolTextOutlineStroke", GroupName = "NinjaScriptGeneral", Order = 6)]
		public Stroke OutlineStroke { get; set; }

		[Display(Order = 1)]
		public ChartAnchor StartAnchor { get; set; }

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (areaBrushDevice != null)
				areaBrushDevice.RenderTarget = null;
		}

		public override IEnumerable<AlertConditionItem> GetAlertConditionItems()
		{
			yield return new AlertConditionItem 
			{
				Name					= Custom.Resource.NinjaScriptDrawingToolRegion,
				ShouldOnlyDisplayName	= true,
			};
		}

		public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
		{
			switch (DrawingState)
			{
				case DrawingState.Building	: return Cursors.Pen;
				case DrawingState.Editing	: return IsLocked ? Cursors.No : Mode == RegionHighlightMode.Time ? Cursors.SizeWE : Cursors.SizeNS;
				case DrawingState.Moving	: return IsLocked ? Cursors.No : Cursors.SizeAll;
				default:
					Point		startAnchorPixelPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
					ChartAnchor	closest					= GetClosestAnchor(chartControl, chartPanel, chartScale, cursorSensitivity, point);
					if (closest != null)
					{
						if (IsLocked)
							return Cursors.Arrow;
						return Mode == RegionHighlightMode.Time ? Cursors.SizeWE : Cursors.SizeNS;
					}

					Point	endAnchorPixelPoint	= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
					Vector	totalVector			= endAnchorPixelPoint - startAnchorPixelPoint;
					if(MathHelper.IsPointAlongVector(point, startAnchorPixelPoint, totalVector, cursorSensitivity))
						return IsLocked ? Cursors.Arrow : Cursors.SizeAll;

					// check if cursor is along region edges
					foreach (Point anchorPoint in new[] {startAnchorPixelPoint, endAnchorPixelPoint})
					{
						if (Mode == RegionHighlightMode.Price && Math.Abs(anchorPoint.Y - point.Y) <= cursorSensitivity)
							return IsLocked ? Cursors.Arrow : Cursors.SizeAll; 
						if (Mode == RegionHighlightMode.Time && Math.Abs(anchorPoint.X - point.X) <= cursorSensitivity)
							return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
					}
					return null;
			}
		}

		public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel	chartPanel	= chartControl.ChartPanels[PanelIndex];
			Point		startPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point		endPoint	= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);

			double		middleX		= chartPanel.X + chartPanel.W / 2;
			double		middleY		= chartPanel.Y + chartPanel.H / 2;
			Point		midPoint	= new Point((startPoint.X + endPoint.X) / 2, (startPoint.Y + endPoint.Y) / 2);
			return new[] { startPoint, midPoint, endPoint }.Select(p => Mode == RegionHighlightMode.Time ? new Point(p.X, middleY) : new Point(middleX, p.Y)).ToArray();
		}

		public override IEnumerable<Condition> GetValidAlertConditions()
		{
			return new[] { Condition.CrossInside, Condition.CrossOutside };
		}

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values,
													ChartControl chartControl, ChartScale chartScale)
		{
			double		minPrice	= Anchors.Min(a => a.Price);
			double		maxPrice	= Anchors.Max(a => a.Price);
			DateTime	minTime		= Anchors.Min(a => a.Time);
			DateTime	maxTime		= Anchors.Max(a => a.Time);

			// note, time region higlight x will always be a cross from moving linearly. until someone builds a time machine anyway
			// no need for lookback/cross check so just check first (most recent) value
			if (Mode == RegionHighlightMode.Time)
			{
				DateTime vt = values[0].Time;
				return condition == Condition.CrossInside ? vt > minTime && vt <= maxTime : vt > minTime && vt < maxTime;
			}

			Predicate<ChartAlertValue> predicate = v =>
			{
				bool isInside = Mode == RegionHighlightMode.Time ? v.Time >= minTime && v.Time <= maxTime : v.Value >= minPrice && v.Value <= maxPrice;
				return condition == Condition.CrossInside ? isInside : !isInside;
			};
			return MathHelper.DidPredicateCross(values, predicate);
		}

		public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (DrawingState == DrawingState.Building)
				return true;
			if (Mode == RegionHighlightMode.Time)
			{
				if (Anchors.Any(a => a.Time >= firstTimeOnChart && a.Time <= lastTimeOnChart))
					return true;
				// check crossovers
				if (StartAnchor.Time <= firstTimeOnChart && EndAnchor.Time >= lastTimeOnChart)
					return true;
				if (EndAnchor.Time <= firstTimeOnChart && StartAnchor.Time >= lastTimeOnChart)
					return true;
				return false;
			}

			// check if active y range highlight is on scale or cross through
			if (Anchors.Any(a => a.Price <= chartScale.MaxValue && a.Price >= chartScale.MinValue))
				return true;
			return StartAnchor.Price <= chartScale.MinValue && EndAnchor.Price >= chartScale.MaxValue || EndAnchor.Price <= chartScale.MinValue && StartAnchor.Price >= chartScale.MaxValue;
		}

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;

			if (!IsVisible)
				return;

				foreach (ChartAnchor anchor in Anchors)
				{
					MinValue = Math.Min(anchor.Price, MinValue);
					MaxValue = Math.Max(anchor.Price, MaxValue);
				}
		}

		public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:

					if (Mode == RegionHighlightMode.Price)
						dataPoint.Time = chartControl.FirstTimePainted.AddSeconds((chartControl.LastTimePainted - chartControl.FirstTimePainted).TotalSeconds / 2);
					else 
						dataPoint.Price = chartScale.MinValue + chartScale.MaxMinusMin / 2;

					if (StartAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(StartAnchor);
						StartAnchor.IsEditing = false;
						dataPoint.CopyDataValues(EndAnchor);
					}
					else if (EndAnchor.IsEditing)
					{
						if (Mode == RegionHighlightMode.Price)
						{
							dataPoint.Time		= StartAnchor.Time;
							dataPoint.SlotIndex	= StartAnchor.SlotIndex;
						}
						else 
							dataPoint.Price = StartAnchor.Price;

						dataPoint.CopyDataValues(EndAnchor);
						EndAnchor.IsEditing = false;
					}
					if (!StartAnchor.IsEditing && !EndAnchor.IsEditing)
					{
						DrawingState	= DrawingState.Normal;
						IsSelected		= false;
					}
					break;
				case DrawingState.Normal:
					Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
					editingAnchor = GetClosestAnchor(chartControl, chartPanel, chartScale, cursorSensitivity, point);
					if (editingAnchor != null)
					{
						editingAnchor.IsEditing = true;
						DrawingState			= DrawingState.Editing;
					}
					else
					{
						if (GetCursor(chartControl, chartPanel, chartScale, point) == Cursors.SizeAll)
							DrawingState = DrawingState.Moving;
						else if (GetCursor(chartControl, chartPanel, chartScale, point) == Cursors.SizeWE || GetCursor(chartControl, chartPanel, chartScale, point) == Cursors.SizeNS)
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
			if (DrawingState == DrawingState.Building && EndAnchor.IsEditing)
			{

				if (Mode == RegionHighlightMode.Price)
					dataPoint.Time = chartControl.FirstTimePainted.AddSeconds((chartControl.LastTimePainted - chartControl.FirstTimePainted).TotalSeconds / 2);
				else 
					dataPoint.Price = chartScale.MinValue + chartScale.MaxMinusMin / 2;
			
				dataPoint.CopyDataValues(EndAnchor);
				//Point buildAdjusted = Mode == RegionHighlightMode.Time ? new Point(point.X, startAnchorPoint.Y) : new Point(startAnchorPoint.X, point.Y);
				//EndAnchor.UpdateFromPoint(buildAdjusted, chartControl, chartScale);
			}
			else if (DrawingState == DrawingState.Editing && editingAnchor != null)
				dataPoint.CopyDataValues(editingAnchor);
			else if (DrawingState == DrawingState.Moving)
				foreach (ChartAnchor anchor in Anchors)
					anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
		}

		public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (DrawingState == DrawingState.Building)
				return;

			DrawingState		= DrawingState.Normal;
			editingAnchor		= null;
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				AnchorLineStroke		= new Stroke(Brushes.DarkGray, DashStyleHelper.Dash, 1f);
				AreaBrush				= Brushes.Goldenrod;
				AreaOpacity				= 25;
				DrawingState			= DrawingState.Building;
				EndAnchor				= new ChartAnchor { DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchorEnd, IsEditing = true, DrawingTool = this };
				OutlineStroke			= new Stroke(Brushes.Goldenrod, 2f);
				StartAnchor				= new ChartAnchor { DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchorStart, IsEditing = true, DrawingTool = this };
				ZOrderType				= DrawingToolZOrder.AlwaysDrawnFirst;
			}
			else if (State == State.Terminated)
				Dispose();
		}

		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			//Allow user to change ZOrder when manually drawn on chart
			if(!hasSetZOrder && !StartAnchor.IsNinjaScriptDrawn)
			{
				ZOrderType	= DrawingToolZOrder.Normal;
				ZOrder		= ChartPanel.ChartObjects.Min(z => z.ZOrder) - 1;
				hasSetZOrder = true;
			}
			RenderTarget.AntialiasMode	= SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			Stroke outlineStroke		= OutlineStroke;
			outlineStroke.RenderTarget	= RenderTarget;
			ChartPanel	chartPanel		= chartControl.ChartPanels[PanelIndex];

			// recenter region anchors to always be onscreen/centered
			double middleX				= chartPanel.X + chartPanel.W / 2d;
			double middleY				= chartPanel.Y + chartPanel.H / 2d;

			if (Mode == RegionHighlightMode.Price)
			{
				StartAnchor.UpdateXFromPoint(new Point(middleX, 0), chartControl, chartScale);
				EndAnchor.UpdateXFromPoint(new Point(middleX, 0), chartControl, chartScale);
			}
			else 
			{
				StartAnchor.UpdateYFromDevicePoint(new Point(0, middleY), chartScale);
				EndAnchor.UpdateYFromDevicePoint(new Point(0, middleY), chartScale);
			}

			Point		startPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point		endPoint	= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			double		width		= endPoint.X - startPoint.X;
		
			AnchorLineStroke.RenderTarget	= RenderTarget;

			if (!IsInHitTest && AreaBrush != null)
			{
				if (areaBrushDevice.Brush == null)
				{
					Brush brushCopy			= areaBrush.Clone();
					brushCopy.Opacity		= areaOpacity / 100d; 
					areaBrushDevice.Brush	= brushCopy;
				}
				areaBrushDevice.RenderTarget = RenderTarget;
			}
			else
			{
				areaBrushDevice.RenderTarget = null;
				areaBrushDevice.Brush = null;
			}

			// align to full pixel to avoid unneeded aliasing
			float strokePixAdjust = Math.Abs(outlineStroke.Width % 2d).ApproxCompare(0) == 0 ? 0.5f : 0f;

			SharpDX.RectangleF rect = Mode == RegionHighlightMode.Time ? 
				new SharpDX.RectangleF((float) startPoint.X + strokePixAdjust, ChartPanel.Y - outlineStroke.Width + strokePixAdjust, 
										(float) width, chartPanel.Y + chartPanel.H + outlineStroke.Width * 2) : 
				new SharpDX.RectangleF(chartPanel.X - outlineStroke.Width + strokePixAdjust, (float)startPoint.Y + strokePixAdjust, 
										chartPanel.X + chartPanel.W + outlineStroke.Width * 2, (float)(endPoint.Y - startPoint.Y));

			if (!IsInHitTest && areaBrushDevice.BrushDX != null)
				RenderTarget.FillRectangle(rect, areaBrushDevice.BrushDX);

			SharpDX.Direct2D1.Brush tmpBrush = IsInHitTest ? chartControl.SelectionBrush : outlineStroke.BrushDX;
			RenderTarget.DrawRectangle(rect, tmpBrush, outlineStroke.Width, outlineStroke.StrokeStyle);

			if (IsSelected)
			{
				tmpBrush = IsInHitTest ? chartControl.SelectionBrush : AnchorLineStroke.BrushDX;
				RenderTarget.DrawLine(startPoint.ToVector2(), endPoint.ToVector2(), tmpBrush, AnchorLineStroke.Width, AnchorLineStroke.StrokeStyle);
			}
		}
	}

	/// <summary>
	/// Represents an interface that exposes information regarding a Region Highlight X IDrawingTool.
	/// </summary>
	[CLSCompliant(false)]
	public class RegionHighlightX : RegionHighlightBase
	{
		public override object Icon { get { return Gui.Tools.Icons.DrawRegionHighlightX; } }

		protected override void OnStateChange()
		{
			base.OnStateChange();
			if (State == State.SetDefaults)
			{
				Name							= Custom.Resource.NinjaScriptDrawingToolRegionHiglightX;
				Mode							= RegionHighlightMode.Time;
				StartAnchor	.IsYPropertyVisible	= false;
				EndAnchor	.IsYPropertyVisible	= false;
			}
		}
	}

	/// <summary>
	/// Represents an interface that exposes information regarding a Region Highlight Y IDrawingTool.
	/// </summary>
	[CLSCompliant(false)]
	public class RegionHighlightY : RegionHighlightBase
	{
		public override object Icon { get { return Gui.Tools.Icons.DrawRegionHighlightY; } }

		protected override void OnStateChange()
		{
			base.OnStateChange();
			if (State == State.SetDefaults)
			{
				Name								= Custom.Resource.NinjaScriptDrawingToolRegionHiglightY;
				Mode								= RegionHighlightMode.Price;
				StartAnchor	.IsXPropertiesVisible	= false;
				EndAnchor	.IsXPropertiesVisible	= false;
			}
		}
	}
	
	public static partial class Draw
	{
		private static readonly Brush	defaultRegionBrush		= Brushes.Goldenrod;
		private const			int		defaultRegionOpacity	= 25;

		private static T RegionHighlightCore<T>(NinjaScriptBase owner, string tag,
			bool isAutoScale,
			int startBarsAgo, DateTime startTime, double startY,
			int endBarsAgo, DateTime endTime, double endY,
			Brush brush, Brush areaBrush, int areaOpacity, bool isGlobal, string templateName) where T : RegionHighlightBase
		{
			if (owner == null)
				throw new ArgumentException("owner");

			if (string.IsNullOrWhiteSpace(tag))
				throw new ArgumentException(@"tag cant be null or empty", "tag");

			if (isGlobal && tag[0] != GlobalDrawingToolManager.GlobalDrawingToolTagPrefix)
				tag = GlobalDrawingToolManager.GlobalDrawingToolTagPrefix + tag;

			T regionHighlight = DrawingTool.GetByTagOrNew(owner, typeof(T), tag, templateName) as T;
			if (regionHighlight == null)
				return null;

			RegionHighlightMode mode = regionHighlight.Mode = typeof(T) == typeof(RegionHighlightX) ? RegionHighlightMode.Time : RegionHighlightMode.Price;

			DrawingTool.SetDrawingToolCommonValues(regionHighlight, tag, isAutoScale, owner, isGlobal);

			ChartAnchor	startAnchor;
			ChartAnchor	endAnchor;

			if (mode == RegionHighlightMode.Time)
			{
				startAnchor = DrawingTool.CreateChartAnchor(owner, startBarsAgo, startTime, startY);
				endAnchor	= DrawingTool.CreateChartAnchor(owner, endBarsAgo, endTime, endY);
			}
			else
			{
				// just create on current bar
				startAnchor = DrawingTool.CreateChartAnchor(owner, 0, owner.Time[0], startY);
				endAnchor	= DrawingTool.CreateChartAnchor(owner, 0, owner.Time[0], endY);
			}

			startAnchor.CopyDataValues(regionHighlight.StartAnchor);
			endAnchor.CopyDataValues(regionHighlight.EndAnchor);
			
			// brushes can be null when using a templateName
			if (regionHighlight.AreaBrush != null && areaBrush != null)
				regionHighlight.AreaBrush = areaBrush.Clone();

			if (areaOpacity >= 0)
				regionHighlight.AreaOpacity	= areaOpacity;
			if (brush != null)
				regionHighlight.OutlineStroke = new Stroke(brush);

			regionHighlight.SetState(State.Active);
			return regionHighlight;
		}

		/// <summary>
		/// Draws a region highlight x on a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		[CLSCompliant(false)]
		public static RegionHighlightX RegionHighlightX(NinjaScriptBase owner, string tag, DateTime startTime, DateTime endTime, Brush brush)
		{
			return RegionHighlightCore<RegionHighlightX>(owner, tag, false, int.MinValue, startTime, 0, int.MinValue, endTime, 0, brush, defaultRegionBrush, defaultRegionOpacity, false, null);
		}

		/// <summary>
		/// Draws a region highlight x on a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		[CLSCompliant(false)]
		public static RegionHighlightX RegionHighlightX(NinjaScriptBase owner, string tag, int startBarsAgo, int endBarsAgo, Brush brush)
		{
			return RegionHighlightCore<RegionHighlightX>(owner, tag, false, startBarsAgo, Core.Globals.MinDate, 0, endBarsAgo, Core.Globals.MinDate, 0, brush, defaultRegionBrush, defaultRegionOpacity, false, null);
		}

		/// <summary>
		/// Draws a region highlight x on a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		[CLSCompliant(false)]
		public static RegionHighlightX RegionHighlightX(NinjaScriptBase owner, string tag, DateTime startTime, DateTime endTime, Brush brush, Brush areaBrush, int areaOpacity)
		{
			return RegionHighlightCore<RegionHighlightX>(owner, tag, false, int.MinValue, startTime, 0, int.MinValue, endTime, 0, brush, areaBrush, areaOpacity, false, null);
		}

		/// <summary>
		/// Draws a region highlight x on a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		[CLSCompliant(false)]
		public static RegionHighlightX RegionHighlightX(NinjaScriptBase owner, string tag, int startBarsAgo, int endBarsAgo, Brush brush, Brush areaBrush, int areaOpacity)
		{
			return RegionHighlightCore<RegionHighlightX>(owner, tag, false, startBarsAgo, Core.Globals.MinDate, 0, endBarsAgo, Core.Globals.MinDate, 0, brush, areaBrush, areaOpacity, false, null);
		}

		/// <summary>
		/// Draws a region highlight x on a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		[CLSCompliant(false)]
		public static RegionHighlightX RegionHighlightX(NinjaScriptBase owner, string tag, DateTime startTime, DateTime endTime, bool isGlobal, string templateName)
		{
			return RegionHighlightCore<RegionHighlightX>(owner, tag, false, int.MinValue, startTime, 0, int.MinValue, endTime, 0, null, null, -1, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a region highlight x on a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		[CLSCompliant(false)]
		public static RegionHighlightX RegionHighlightX(NinjaScriptBase owner, string tag, int startBarsAgo, int endBarsAgo, bool isGlobal, string templateName)
		{
			return RegionHighlightCore<RegionHighlightX>(owner, tag, false, startBarsAgo, Core.Globals.MinDate, 0, endBarsAgo, Core.Globals.MinDate, 0, null, null, -1, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a region highlight y on a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		[CLSCompliant(false)]
		public static RegionHighlightY RegionHighlightY(NinjaScriptBase owner, string tag, double startY, double endY, Brush brush)
		{
			return RegionHighlightCore<RegionHighlightY>(owner, tag, false, 0, Core.Globals.MinDate, startY, 0, Core.Globals.MinDate, endY, brush, defaultRegionBrush, defaultRegionOpacity, false, null);
		}

		/// <summary>
		/// Draws a region highlight y on a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		[CLSCompliant(false)]
		public static RegionHighlightY RegionHighlightY(NinjaScriptBase owner, string tag, bool isAutoScale, double startY, double endY, Brush brush, Brush areaBrush, int areaOpacity)
		{
			return RegionHighlightCore<RegionHighlightY>(owner, tag, isAutoScale, 0, Core.Globals.MinDate, startY, 0, Core.Globals.MinDate, endY, brush, areaBrush, areaOpacity, false, null);
		}

		/// <summary>
		/// Draws a region highlight y on a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		[CLSCompliant(false)]
		public static RegionHighlightY RegionHighlightY(NinjaScriptBase owner, string tag, double startY, double endY, bool isGlobal, string templateName)
		{
			return RegionHighlightCore<RegionHighlightY>(owner, tag, false, 0, Core.Globals.MinDate, startY, 0, Core.Globals.MinDate, endY, null, null, -1, isGlobal, templateName);
		}
	}
}
