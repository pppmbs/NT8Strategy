// 
// Copyright (C) 2021, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
#endregion

namespace NinjaTrader.NinjaScript.DrawingTools
{
	/// <summary>
	/// Represents an interface that exposes information regarding an Arc IDrawingTool.
	/// </summary>
	public class Arc : Line
	{
		private				SharpDX.Direct2D1.PathGeometry	arcGeometry;
		private				Brush							areaBrush;
		private	readonly	DeviceBrush						areaBrushDevice		= new DeviceBrush();
		private				int								areaOpacity;
		private				Point							cachedStartPoint;
		private				Point							cachedEndPoint;


		[Display(ResourceType = typeof(Custom.Resource), GroupName = "NinjaScriptGeneral", Name = "NinjaScriptDrawingToolArc", Order = 99)]
		public Stroke ArcStroke { get; set; }

		[XmlIgnore] 
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolShapesAreaBrush", GroupName = "NinjaScriptGeneral", Order = 1)]
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
					areaBrush.Freeze();
				}
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
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolAreaOpacity", GroupName = "NinjaScriptGeneral", Order = 2)]
		public int AreaOpacity
		{
			get { return areaOpacity; }
			set
			{
				areaOpacity = Math.Max(0, Math.Min(100, value));
				areaBrushDevice.Brush = null;
			}
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (areaBrushDevice != null)
				areaBrushDevice.RenderTarget = null;
			if (arcGeometry != null)
				arcGeometry.Dispose();
		}

		public override System.Collections.Generic.IEnumerable<AlertConditionItem> GetAlertConditionItems()
		{
			yield return new AlertConditionItem 
			{
				Name					= Name,
				ShouldOnlyDisplayName	= true,
			};
		}

		public override System.Collections.Generic.IEnumerable<Condition> GetValidAlertConditions()
		{
			return new[]{ Condition.CrossInside, Condition.CrossOutside };
		}

		public override object Icon { get { return Gui.Tools.Icons.DrawArc; } }

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values,
													ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel chartPanel	= chartControl.ChartPanels[PanelIndex];
			double barX				= chartControl.GetXByTime(values[0].Time);
			double barY				= chartScale.GetYByValue(values[0].Value);
			Point startPoint		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point endPoint			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point barPoint			= new Point(barX, barY);

			if (arcGeometry == null || arcGeometry.IsDisposed)
				UpdateArcGeometry(chartControl,  chartPanel, chartScale);

			// Bars have not yet reached edge of drawing tool
			if (barX < Math.Min(startPoint.X, endPoint.X))
				return false;

			// Do two things, make sure the point is on the right side of the line (the side arc is sweeped into),
			// and if it is, then check if it is in arc geo
			MathHelper.PointLineLocation ptLineLocation = MathHelper.GetPointLineLocation(startPoint, endPoint, barPoint);
			// If its not right/above , its past the line the arc sweeps on, so ignore
			if (ptLineLocation != MathHelper.PointLineLocation.RightOrBelow)
				return false;

			// Our only conditions are cross inside/outside
			Predicate<ChartAlertValue> arcPredicate = v => 
			{
				if (v.Time == Core.Globals.MinDate || v.Time == Core.Globals.MaxDate)
					return false;
				if (v.Value == double.MinValue)
					return false;

				double bX		= chartControl.GetXByTime(v.Time);
				double bY		= chartScale.GetYByValue(v.Value);
				Point bp		= new Point(bX, bY);
				bool isInGeo	= arcGeometry.FillContainsPoint(bp.ToVector2());
				return condition == Condition.CrossInside ? isInGeo : !isInGeo;
			};
			return MathHelper.DidPredicateCross(values, arcPredicate);
		}

		public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
		{
			Cursor cursor = base.GetCursor(chartControl, chartPanel, chartScale, point);
			if (cursor != null)
				return cursor;

			if (arcGeometry == null || arcGeometry.IsDisposed)
				UpdateArcGeometry(chartControl, chartPanel, chartScale);

			return arcGeometry.StrokeContainsPoint(point.ToVector2(), 15)
				? (IsLocked && DrawingState != DrawingState.Normal ? Cursors.No : Cursors.SizeAll)
				: null;
		}

		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel panel = chartControl.ChartPanels[PanelIndex];
			UpdateArcGeometry(chartControl, panel, chartScale);
			base.OnRender(chartControl, chartScale);

			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

			if (AreaBrush != null && !IsInHitTest)
			{
				areaBrushDevice.RenderTarget = RenderTarget;
				if (areaBrushDevice.Brush == null)
				{
					Brush brushCopy			= AreaBrush.Clone();
					brushCopy.Opacity		= areaOpacity / 100d;
					areaBrushDevice.Brush	= brushCopy;
				}
				RenderTarget.FillGeometry(arcGeometry, areaBrushDevice.BrushDX);
			}

			ArcStroke.RenderTarget = RenderTarget;
			SharpDX.Direct2D1.Brush tmpBrush = IsInHitTest ? chartControl.SelectionBrush : ArcStroke.BrushDX;
			RenderTarget.DrawGeometry(arcGeometry, tmpBrush, ArcStroke.Width, ArcStroke.StrokeStyle);
		}
		
		protected override void OnStateChange()
		{
			base.OnStateChange();
			if (State == State.SetDefaults)
			{
				Name			= Custom.Resource.NinjaScriptDrawingToolArc;
				LineType		= ChartLineType.Line;

				Stroke			= new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Solid, 2f, 50);
				ArcStroke		= new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Solid, 2f);
				AreaBrush		= Brushes.CornflowerBlue;
				AreaOpacity		= 40;
			}
			else if (State == State.Terminated)
			{
				if (arcGeometry != null)
					arcGeometry.Dispose();
				arcGeometry = null;
				if (areaBrushDevice != null)
					areaBrushDevice.RenderTarget = null;
			}
		}

		public override bool SupportsAlerts { get { return true; } }

		private void UpdateArcGeometry(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale)
		{
			Point startPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point endPoint		= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			if (arcGeometry != null && startPoint == cachedStartPoint && endPoint == cachedEndPoint)
				return;
			
			cachedEndPoint		= endPoint;
			cachedStartPoint	= startPoint;

			if (arcGeometry != null && !arcGeometry.IsDisposed)
				arcGeometry.Dispose();

			Vector lineVec		= endPoint - startPoint;
			float width			= Math.Abs((float)lineVec.X);
			float height		= Math.Abs((float)lineVec.Y);

			SharpDX.Direct2D1.ArcSegment arcSegment = new SharpDX.Direct2D1.ArcSegment
			{
				ArcSize			= SharpDX.Direct2D1.ArcSize.Small,
				Point			= new SharpDX.Vector2((float) endPoint.X, (float) endPoint.Y),
				SweepDirection	= SharpDX.Direct2D1.SweepDirection.CounterClockwise,
				Size			= new SharpDX.Size2F(width, height)
			};

			// Create the arc between the line two end points
			arcGeometry = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
			SharpDX.Direct2D1.GeometrySink geometrySink = arcGeometry.Open();
			geometrySink.BeginFigure(new SharpDX.Vector2((float)startPoint.X, (float)startPoint.Y), SharpDX.Direct2D1.FigureBegin.Filled);
			geometrySink.AddArc(arcSegment);
			geometrySink.EndFigure(SharpDX.Direct2D1.FigureEnd.Open);
			geometrySink.Close();
		}
	}

	public static partial class Draw
	{
		private static Arc ArcCore(NinjaScriptBase owner, bool isAutoScale, string tag,
							int startBarsAgo, DateTime startTime, double startY, int endBarsAgo, DateTime endTime, double endY,
							Brush brush, DashStyleHelper dashStyle, int width, bool isGlobal, string templateName)
		{
			if (owner == null)
				throw new ArgumentException("owner");

			if (startTime == Core.Globals.MinDate && endTime == Core.Globals.MinDate && startBarsAgo == int.MinValue && endBarsAgo == int.MinValue)
				throw new ArgumentException("bad start/end date/time");

			if (string.IsNullOrWhiteSpace(tag))
				throw new ArgumentException("tag cant be null or empty", "tag");

			if (isGlobal && tag[0] != GlobalDrawingToolManager.GlobalDrawingToolTagPrefix)
				tag = GlobalDrawingToolManager.GlobalDrawingToolTagPrefix + tag;

			Arc newArc = DrawingTool.GetByTagOrNew(owner, typeof(Arc), tag, templateName) as Arc;
			
			if (newArc == null)
				return null;

			DrawingTool.SetDrawingToolCommonValues(newArc, tag, isAutoScale, owner, isGlobal);

			// dont nuke existing anchor refs on the instance
			ChartAnchor startAnchor	= DrawingTool.CreateChartAnchor(owner, startBarsAgo, startTime, startY);
			ChartAnchor endAnchor	= DrawingTool.CreateChartAnchor(owner, endBarsAgo, endTime, endY);
			
			startAnchor.CopyDataValues(newArc.StartAnchor);
			endAnchor.CopyDataValues(newArc.EndAnchor);
			
			if (brush != null)
			{
				newArc.Stroke		= new Stroke(brush, dashStyle, width, 50) { RenderTarget = newArc.Stroke.RenderTarget };
				newArc.ArcStroke	= new Stroke(brush, dashStyle, width) { RenderTarget = newArc.ArcStroke.RenderTarget };
			}
			newArc.SetState(State.Active);
			return newArc;
		}

		/// <summary>
		/// Draws an arc.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static Arc Arc(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, Brush brush)
		{
			return ArcCore(owner, false, tag, startBarsAgo, Core.Globals.MinDate, startY,
				endBarsAgo, Core.Globals.MinDate, endY, brush, DashStyleHelper.Solid, 1, false, null);
		}

		/// <summary>
		/// Draws an arc.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static Arc Arc(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY, Brush brush)
		{
			return ArcCore(owner, false, tag, int.MinValue, startTime, startY,
				int.MinValue, endTime, endY, brush, DashStyleHelper.Solid, 1, false, null);
		}

		/// <summary>
		/// Draws an arc.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <returns></returns>
		public static Arc Arc(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo,
								double endY, Brush brush, DashStyleHelper dashStyle, int width)
		{
			return ArcCore(owner, false, tag, startBarsAgo, Core.Globals.MinDate, startY,
				endBarsAgo, Core.Globals.MinDate, endY, brush, dashStyle, width, false, null);
		}

		/// <summary>
		/// Draws an arc.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <returns></returns>
		public static Arc Arc(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime,
								double endY, Brush brush, DashStyleHelper dashStyle, int width)
		{
			return ArcCore(owner, isAutoScale, tag, int.MinValue, startTime, startY,
													int.MinValue, endTime, endY, brush, dashStyle, width, false, null);
		}

		/// <summary>
		/// Draws an arc.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static Arc Arc(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, 
								double endY, Brush brush, DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ArcCore(owner, isAutoScale, tag, startBarsAgo, 
					Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, brush, dashStyle, width, false, null));
		}

		/// <summary>
		/// Draws an arc.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static Arc Arc(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime, 
								double endY, Brush brush, DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ArcCore(owner, isAutoScale, tag, int.MinValue, 
					startTime, startY, int.MinValue, endTime, endY, brush, dashStyle, width, false, null));
		}

		/// <summary>
		/// Draws an arc.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static Arc Arc(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, bool isGlobal, string templateName)
		{
			return ArcCore(owner, false, tag, startBarsAgo, Core.Globals.MinDate, startY,
				endBarsAgo, Core.Globals.MinDate, endY, null, DashStyleHelper.Solid, 1, isGlobal, templateName);
		}

		/// <summary>
		/// Draws an arc.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static Arc Arc(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY, bool isGlobal, string templateName)
		{
			return ArcCore(owner, false, tag, int.MinValue, startTime, startY,
				int.MinValue, endTime, endY, null, DashStyleHelper.Solid, 1, isGlobal, templateName);
		}
	}
}
