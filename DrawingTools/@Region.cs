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
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.DrawingTools
{
	// Region is a drawing tool that is non-interactive on the chart, it is only usable via NinjaScript
	/// <summary>
	/// Represents an interface that exposes information regarding a Region IDrawingTool.
	/// </summary>
	public class Region : DrawingTool
	{
		private int									areaOpacity;
		private Brush								areaBrush;
		private readonly DeviceBrush				areaBrushDevice = new DeviceBrush();
		
		public ChartAnchor StartAnchor 	{ get; set; }
		public ChartAnchor EndAnchor	{ get; set; }
		
		[Browsable(false)]
		[XmlIgnore]
		public ISeries<double> Series1 { get; set; }
		
		[Browsable(false)]
		[XmlIgnore]
		public ISeries<double> Series2 { get; set; }
		
		[Browsable(false)]
		public double Price { get; set; }
		
		[Browsable(false)]
		public int Displacement { get; set; }

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolShapesAreaBrush", GroupName = "NinjaScriptGeneral", Order = 4)]
		public Brush AreaBrush
		{
			get { return areaBrush; }
			set
			{
				areaBrush = value;
				if (areaBrush == null)
					return;
				if (areaBrush.IsFrozen)
					areaBrush = areaBrush.Clone();
				areaBrush.Opacity = areaOpacity / 100d;
				areaBrush.Freeze();
				areaBrushDevice.Brush = null;
			}
		}

		[Browsable(false)]
		public string AreaBrushSerialize
		{
			get { return  Serialize.BrushToString(AreaBrush); }
			set { AreaBrush = Serialize.StringToBrush(value); }
		}

		[Range(0,100)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolAreaOpacity", GroupName = "NinjaScriptGeneral", Order = 5)]
		public int AreaOpacity
		{
			get { return areaOpacity; }
			set
			{
				areaOpacity = Math.Max(0, Math.Min(100, value));
				areaBrushDevice.Brush = null;
			}
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolTextOutlineStroke", GroupName = "NinjaScriptGeneral", Order = 6)]
		public Stroke OutlineStroke { get; set; }
		
		public override IEnumerable<ChartAnchor> Anchors 
		{
			get { return new[] { StartAnchor, EndAnchor }; }
		}
		
		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (areaBrushDevice != null)
				areaBrushDevice.RenderTarget = null;
		}
		
		public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (!(AttachedTo.ChartObject is Gui.NinjaScript.IChartBars) || (AttachedTo.ChartObject as Gui.NinjaScript.IChartBars).ChartBars == null)
				return false;

			// not setup yet
			if (!StartAnchor.IsNinjaScriptDrawn || !EndAnchor.IsNinjaScriptDrawn)
				return false;

			DateTime startTime	= StartAnchor.Time;
			DateTime endTime	= EndAnchor.Time;

			return startTime >= firstTimeOnChart || endTime <= lastTimeOnChart || startTime < firstTimeOnChart && endTime > lastTimeOnChart;
		}

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= Custom.Resource.NinjaScriptDrawingToolRegion;
				Name						= Custom.Resource.NinjaScriptDrawingToolRegion;
				DisplayOnChartsMenus		= false;
				IgnoresUserInput			= true;
				StartAnchor 				= new ChartAnchor { IsYPropertyVisible = false, IsXPropertiesVisible = false };
				EndAnchor 					= new ChartAnchor { IsYPropertyVisible = false, IsXPropertiesVisible = false };
				StartAnchor.DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorStart;
				EndAnchor.DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorEnd;
				AreaBrush 					= Brushes.DarkCyan;
				OutlineStroke 				= new Stroke(Brushes.Goldenrod);
				AreaOpacity					= 40;
				ZOrderType					= DrawingToolZOrder.AlwaysDrawnFirst;
			}
			else if (State == State.Terminated)
				Dispose();
		}

		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (Series1 == null)
				return;
			
			NinjaScriptBase		nsb			= AttachedTo.ChartObject as NinjaScriptBase;
			ChartBars			chartBars	= (AttachedTo.ChartObject as Gui.NinjaScript.IChartBars).ChartBars;

			if (nsb == null || chartBars == null || Math.Abs(Series1.Count - chartBars.Count) > 1)
				return;
			
			int startBarIdx;
			int endBarIdx;
			
			if(chartControl.BarSpacingType == BarSpacingType.TimeBased)
			{
				startBarIdx 	= chartBars.GetBarIdxByTime(chartControl, StartAnchor.Time);
				endBarIdx 		= chartBars.GetBarIdxByTime(chartControl, EndAnchor.Time);
			}
			else
			{
				startBarIdx 	= StartAnchor.DrawnOnBar - StartAnchor.BarsAgo;
				endBarIdx 		= EndAnchor.DrawnOnBar - EndAnchor.BarsAgo;

				if (startBarIdx == endBarIdx)
				{
					startBarIdx 	= chartBars.GetBarIdxByTime(chartControl, StartAnchor.Time);
					endBarIdx 		= chartBars.GetBarIdxByTime(chartControl, EndAnchor.Time);
				}
			}

			int startIdx		= Math.Min(startBarIdx, endBarIdx);
			int endIdx			= Math.Max(startBarIdx, endBarIdx);
			
			// Now cap start/end by visibly painted bars! 
			// If you dont do this it will absolutely crush performance on larger regions
			int firstVisibleIdx	= Math.Max(nsb.BarsRequiredToPlot + Displacement, chartBars.GetBarIdxByTime(chartControl, chartControl.GetTimeByX(0)) - 1);
			int lastVisibleIdx	= Math.Max(chartBars.ToIndex, chartBars.GetBarIdxByTime(chartControl, chartControl.LastTimePainted)) + 1;

			// Update indicies for displacement
			startIdx	= Math.Max(0, Math.Max(firstVisibleIdx, startIdx + Displacement));
			endIdx		= Math.Max(0, Math.Min(endIdx + Displacement, lastVisibleIdx));
			
			// we're completely not visible
			if (startIdx > lastVisibleIdx || endIdx < firstVisibleIdx)
				return;

			/* NOTE: Calling GetValueAt() on an ISeries<double> interface with a concrete
			 type of NinjaScriptBase will get the *bar* value which is not what we want,
			 in this case, default to first values (indicator) series */
			ISeries<double> series1Adjusted = Series1;
			ISeries<double> series2Adjusted = Series2;
			
			NinjaScriptBase series1NsBase = Series1 as NinjaScriptBase;

			if (series1NsBase != null)
				series1Adjusted = series1NsBase.Value;

			if (series1Adjusted == null)
				return;

			NinjaScriptBase series2NsBase = Series2 as NinjaScriptBase;
			if (series2NsBase != null)
				series2Adjusted = series2NsBase.Value;

			// take care to wind the points correctly so our geometry builds as a solid, not flipped inside out
			SharpDX.Vector2[]	points;
			SharpDX.Vector2[]	points2		= new SharpDX.Vector2[0];
			int					pointIdx	= 0;
			int					pointIdx2	= 0;

			if (series2Adjusted == null)
			{
				points = new SharpDX.Vector2[endIdx - startIdx + 1 + 2];
				for (int i = startIdx; i <= endIdx; ++i)
				{
					if (i < Math.Max(0, Displacement) || i > Math.Max(chartBars.Count - (nsb.Calculate == Calculate.OnBarClose ? 2 : 1) + Displacement, endIdx))
						continue;
					
					int		displacedIndex	= Math.Min(chartBars.Count - (nsb.Calculate == Calculate.OnBarClose ? 2 : 1), Math.Max(0, i - Displacement));
					double	seriesValue		= series1Adjusted.GetValueAt(displacedIndex);
					float	y				= chartScale.GetYByValue(seriesValue);
					float	x				= chartControl.BarSpacingType == BarSpacingType.TimeBased || chartControl.BarSpacingType == BarSpacingType.EquidistantMulti && i >= chartBars.Count //i is already displaced
						? chartControl.GetXByTime(chartBars.GetTimeByBarIdx(chartControl, i))
						: chartControl.GetXByBarIndex(chartBars, i);

					double pixXAdjust = x % 1 != 0 ? 0 : 0.5d;
					double pixYAdjust = y % 1 != 0 ? 0 : 0.5d;
					Vector pixelAdjustVec = new Vector(pixXAdjust, pixYAdjust);

					Point adjusted = new Point(x, y) + pixelAdjustVec;
					points[pointIdx] = adjusted.ToVector2();
					++pointIdx;
				}
				
				// cap it end->start
				points[pointIdx].X = chartControl.BarSpacingType == BarSpacingType.TimeBased || chartControl.BarSpacingType == BarSpacingType.EquidistantMulti && endIdx >= chartBars.Count
					? chartControl.GetXByTime(chartBars.GetTimeByBarIdx(chartControl, endIdx))
					: chartControl.GetXByBarIndex(chartBars, endIdx);
				points[pointIdx++].Y = chartScale.GetYByValue(Math.Max(chartScale.MinValue, Math.Min(chartScale.MaxValue, Price)));
			
				points[pointIdx].X = chartControl.BarSpacingType == BarSpacingType.TimeBased || chartControl.BarSpacingType == BarSpacingType.EquidistantMulti && startIdx >= chartBars.Count
					? chartControl.GetXByTime(chartBars.GetTimeByBarIdx(chartControl, startIdx))
					: chartControl.GetXByBarIndex(chartBars, startIdx);
				points[pointIdx++].Y = chartScale.GetYByValue(Math.Max(chartScale.MinValue, Math.Min(chartScale.MaxValue, Price)));
			}
			else
			{
				points	= new SharpDX.Vector2[endIdx - startIdx + 1];
				points2	= new SharpDX.Vector2[endIdx - startIdx + 1];
				// fill clockwise from series1, the	counter clockwise for series 2 for correct point poly winding
				for (int i = startIdx; i <= endIdx; ++i)
				{
					if (i < Math.Max(0, Displacement) || i > Math.Max(chartBars.Count - (nsb.Calculate == Calculate.OnBarClose ? 2 : 1) + Displacement, endIdx))
						continue;
					
					int		displacedIndex	= Math.Min(chartBars.Count - (nsb.Calculate == Calculate.OnBarClose ? 2 : 1), Math.Max(0, i - Displacement));
					float	x				= chartControl.BarSpacingType == BarSpacingType.TimeBased || chartControl.BarSpacingType == BarSpacingType.EquidistantMulti && i >= chartBars.Count //i is already displaced
						? chartControl.GetXByTime(chartBars.GetTimeByBarIdx(chartControl, i))
						: chartControl.GetXByBarIndex(chartBars, i);
					if (!series1Adjusted.IsValidDataPointAt(displacedIndex)) continue;
					double	seriesValue		= series1Adjusted.GetValueAt(displacedIndex);
					float	y				= chartScale.GetYByValue(seriesValue);

					double pixXAdjust = x % 1 != 0 ? 0 : 0.5d;
					double pixYAdjust = y % 1 != 0 ? 0 : 0.5d;

					Vector pixelAdjustVec = new Vector(pixXAdjust, pixYAdjust);

					Point adjusted = new Point(x,y) + pixelAdjustVec;
					points[pointIdx] = adjusted.ToVector2();
					++pointIdx;
					if (!series2Adjusted.IsValidDataPointAt(displacedIndex)) continue;
					seriesValue		= series2Adjusted.GetValueAt(displacedIndex);
					y = chartScale.GetYByValue(seriesValue);
					pixYAdjust = y % 1 != 0 ? 0 : 0.5d;
					pixelAdjustVec = new Vector(pixXAdjust, pixYAdjust);
					adjusted = new Point(x,y) + pixelAdjustVec;
					points2[pointIdx2] = adjusted.ToVector2();
					++pointIdx2;
				}
			}

			if (pointIdx + pointIdx2 > 2)
			{
				RenderTarget.AntialiasMode	= SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
		
				if (OutlineStroke != null)
					OutlineStroke.RenderTarget = RenderTarget;

				if (AreaBrush != null)
				{
					if (areaBrushDevice.Brush == null)
					{
						Brush brushCopy			= areaBrush.Clone();
						brushCopy.Opacity		= areaOpacity / 100d;
						areaBrushDevice.Brush	= brushCopy;
					}
					areaBrushDevice.RenderTarget = RenderTarget;
				}

				SharpDX.Direct2D1.PathGeometry polyGeo	= new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
				SharpDX.Direct2D1.GeometrySink geoSink	= polyGeo.Open();
				double pixXAdjust						= points[0].X % 1 != 0 ? 0 : 0.5d;
				double pixYAdjust						= points[0].Y % 1 != 0 ? 0 : 0.5d;
				Vector pixelAdjustVec					= new Vector(pixXAdjust, pixYAdjust);
				Point startPt							= new Point(points[0].X,points[0].Y) + pixelAdjustVec;
				
				geoSink.BeginFigure(startPt.ToVector2(), SharpDX.Direct2D1.FigureBegin.Filled);
				geoSink.SetFillMode(SharpDX.Direct2D1.FillMode.Winding);
				
				// NOTE: We skip our first point since that is where the path will start
				for (int i = 1; i < pointIdx; i++)
					geoSink.AddLine(points[i]);
				for (int i = pointIdx2 - 1; i >= 0; i--)
					geoSink.AddLine(points2[i]);
				geoSink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
				geoSink.Close();
				
				SharpDX.Direct2D1.Brush tmpBrush = IsInHitTest ? chartControl.SelectionBrush : areaBrushDevice == null ? null : areaBrushDevice.BrushDX;
				if (tmpBrush != null)
					RenderTarget.FillGeometry(polyGeo, tmpBrush);
				
				tmpBrush = IsInHitTest ? chartControl.SelectionBrush : OutlineStroke == null ? null : OutlineStroke.BrushDX;
				if (tmpBrush!= null)
					RenderTarget.DrawGeometry(polyGeo, tmpBrush, OutlineStroke.Width);

				polyGeo.Dispose();
			}	
		}
	}
	
	public static partial class Draw
	{
		private static Region Region(NinjaScriptBase owner, string tag, 
			int startBarsAgo, DateTime startTime, int endBarsAgo, DateTime endTime,
			ISeries<double> series1, ISeries<double> series2, double price,
			Brush outlineBrush, Brush areaBrush, int areaOpacity, int displacement)
		{
			Region region = DrawingTool.GetByTagOrNew(owner, typeof(Region), tag, null) as Region;
			if (region == null)
				return null;

			DrawingTool.SetDrawingToolCommonValues(region, tag, false, owner, false);

			ChartAnchor		startAnchor		= DrawingTool.CreateChartAnchor(owner, startBarsAgo, startTime, 0);
			ChartAnchor		endAnchor		= DrawingTool.CreateChartAnchor(owner, endBarsAgo, endTime, 0);

			startAnchor.CopyDataValues(region.StartAnchor);
			endAnchor.CopyDataValues(region.EndAnchor);
		
			if (series1 == null && series2 == null)
				throw new ArgumentException("At least one series is required");

			region.Series1			= series1;
			region.Series2			= series2;
			region.Price			= price;
			region.Displacement 	= displacement;
			
			region.AreaBrush 		= areaBrush;
			region.AreaOpacity		= areaOpacity;

			region.OutlineStroke	= outlineBrush != null ? new Stroke(outlineBrush) : null;
			
			region.SetState(State.Active);
			region.DrawingState	= DrawingState.Normal;
			region.IsSelected	= false;
			return region;
		}

		/// <summary>
		/// Draws a region on a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="series">Any Series double type object such as an indicator, Close, High, Low etc.. The value of the object will represent a y value</param>
		/// <param name="price">Any double value</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <param name="displacement">An optional parameter which will offset the barsAgo value for the Series double value used to match the desired Displacement.  Default value is 0</param>
		/// <returns></returns>
		public static Region Region(NinjaScriptBase owner, string tag, int startBarsAgo,
			int endBarsAgo, ISeries<double> series, double price, Brush areaBrush, int areaOpacity, int displacement = 0)
		{
			return Region(owner, tag, startBarsAgo, Core.Globals.MinDate, endBarsAgo, Core.Globals.MinDate,
				series, null, price, null, areaBrush, areaOpacity, displacement);
		}

		/// <summary>
		/// Draws a region on a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="series1">Any Series double type object such as an indicator, Close, High, Low etc.. The value of the object will represent a y value.</param>
		/// <param name="series2">Any Series double type object such as an indicator, Close, High, Low etc.. The value of the object will represent a y value.</param>
		/// <param name="outlineBrush">The brush used to color the region outline of draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <param name="displacement">An optional parameter which will offset the barsAgo value for the Series double value used to match the desired Displacement.  Default value is 0</param>
		/// <returns></returns>
		public static Region Region(NinjaScriptBase owner, string tag, int startBarsAgo,
			int endBarsAgo, ISeries<double> series1, ISeries<double> series2, Brush outlineBrush,
			Brush areaBrush, int areaOpacity, int displacement = 0)
		{
			return Region(owner, tag, startBarsAgo, Core.Globals.MinDate, endBarsAgo, Core.Globals.MinDate,
				series1, series2, 0, outlineBrush, areaBrush, areaOpacity, displacement);
		}

		/// <summary>
		/// Draws a region on a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="series">Any Series double type object such as an indicator, Close, High, Low etc.. The value of the object will represent a y value</param>
		/// <param name="price">Any double value</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		public static Region Region(NinjaScriptBase owner, string tag, DateTime startTime,
			DateTime endTime, ISeries<double> series, double price, Brush areaBrush, int areaOpacity)
		{
			return Region(owner, tag, int.MinValue, startTime, int.MinValue, endTime,
				series, null, price, null, areaBrush, areaOpacity, 0);
		}

		/// <summary>
		/// Draws a region on a chart.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="series1">Any Series double type object such as an indicator, Close, High, Low etc.. The value of the object will represent a y value.</param>
		/// <param name="series2">Any Series double type object such as an indicator, Close, High, Low etc.. The value of the object will represent a y value.</param>
		/// <param name="outlineBrush">The brush used to color the region outline of draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		public static Region Region(NinjaScriptBase owner, string tag, DateTime startTime,
			DateTime endTime, ISeries<double> series1, ISeries<double> series2, Brush outlineBrush, Brush areaBrush, int areaOpacity)
		{
			return Region(owner, tag, int.MinValue, startTime, int.MinValue, endTime,
				series1, series2, 0, outlineBrush, areaBrush, areaOpacity, 0);
		}
	}
}
