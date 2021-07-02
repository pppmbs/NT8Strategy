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
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.DrawingTools
{
	/// <summary>
	/// Represents an interface that exposes information regarding a Polygon IDrawingTool.
	/// </summary>
	public class Polygon : DrawingTool
	{
		#region Variables
		private				Brush       areaBrush;
		private readonly	DeviceBrush areaBrushDevice     = new DeviceBrush();
		private				int         areaOpacity;
        private const       double      cursorSensitivity   = 15;
		private				ChartAnchor editingAnchor;
        private             bool        firstTime           = true;
        private             DateTime    lastClick;
		#endregion

		#region Properties
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolShapesAreaBrush", GroupName = "NinjaScriptGeneral", Order = 0)]
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
			get { return Serialize.BrushToString(AreaBrush); }
			set { AreaBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolAreaOpacity", GroupName = "NinjaScriptGeneral", Order = 1)]
		public int AreaOpacity
		{
			get { return areaOpacity; }
			set
			{
				areaOpacity				= Math.Max(0, Math.Min(100, value));
				areaBrushDevice.Brush	= null;
			}
		}

		public override object Icon { get { return Gui.Tools.Icons.DrawPolygon; } }

		[Browsable(false)]
		[SkipOnCopyTo(true), ExcludeFromTemplate]
		public List<ChartAnchor> ChartAnchors { get; set; }

		[Display(Order = 0)]
		[SkipOnCopyTo(true), ExcludeFromTemplate]
		public ChartAnchor StartAnchor
		{
			get
			{
				if (ChartAnchors == null || ChartAnchors.Count == 0)
					return new ChartAnchor { DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchorStart, IsEditing = true, DrawingTool = this };
				else return ChartAnchors[0];
			}
			set
			{
				if (ChartAnchors != null)
				{
					if (ChartAnchors.Count == 0)
						ChartAnchors.Add(value);
					else
						ChartAnchors[0] = value;
				}
			}
		}
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolTextOutlineStroke", GroupName = "NinjaScriptGeneral", Order = 2)]
		public Stroke OutlineStroke							{ get; set; }

		// There always must be a ChartAnchor in here or Polygon won't even start up
		public override IEnumerable<ChartAnchor> Anchors	{ get { return ChartAnchors == null || ChartAnchors.Count == 0 ? new ChartAnchor[] { StartAnchor } : ChartAnchors.ToArray(); } }

		public override bool SupportsAlerts					{ get { return true; } }
		#endregion
		
		public override void CopyTo(NinjaScript ninjascript)
		{
			base.CopyTo(ninjascript);
			
			Polygon p = ninjascript as Polygon;
			if (p != null && ChartAnchors != null)
			{
				p.ChartAnchors.Clear();
				// We have to deep copy our List of Chart Anchors
				foreach(ChartAnchor ca in ChartAnchors)
					p.ChartAnchors.Add(ca.Clone() as ChartAnchor);
			}
		}

		private SharpDX.Direct2D1.PathGeometry CreatePolygonGeometry(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, double pixelAdjust)
		{
			List<SharpDX.Vector2> vectors	= new List<SharpDX.Vector2>();
			Vector pixelAdjustVec			= new Vector(pixelAdjust, pixelAdjust);

			for (int i = 0; i < ChartAnchors.Count; i++)
			{
				Point p = ChartAnchors[i].GetPoint(chartControl, chartPanel, chartScale);
				vectors.Add((p + pixelAdjustVec).ToVector2());

				if (i + 1 < ChartAnchors.Count)
				{
					Point p2 = ChartAnchors[i + 1].GetPoint(chartControl, chartPanel, chartScale);
					vectors.Add((p2 + pixelAdjustVec).ToVector2());
				}
				else if (DrawingState != DrawingState.Building)
					vectors.Add(vectors[0]);
			}

			SharpDX.Direct2D1.PathGeometry pathGeometry = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
			SharpDX.Direct2D1.GeometrySink geometrySink = pathGeometry.Open();
			geometrySink.BeginFigure(vectors[0], SharpDX.Direct2D1.FigureBegin.Filled);
			geometrySink.AddLines(vectors.ToArray());
			geometrySink.EndFigure(SharpDX.Direct2D1.FigureEnd.Open);
			geometrySink.Close(); // calls dispose for you

			return pathGeometry;
		}

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
				Name					= "Polygon",
				ShouldOnlyDisplayName	= true
			};
		}

		public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
		{
			if (DrawingState == DrawingState.Building)
			{
				if (ChartAnchors.Count > 2)
				{
					Point start = ChartAnchors[0].GetPoint(chartControl, chartPanel, chartScale);
					if (point.X >= start.X - cursorSensitivity && point.X <= start.X + cursorSensitivity 
						&& point.Y >= start.Y - cursorSensitivity && point.Y <= start.Y + cursorSensitivity)
						return Cursors.Cross;
				}
				return Cursors.Pen;
			}

			if (DrawingState == DrawingState.Moving)
				return IsLocked ? Cursors.No : Cursors.SizeAll;

			if (DrawingState == DrawingState.Editing && IsLocked)
				return Cursors.No;

			if (GetClosestAnchor(chartControl, chartPanel, chartScale, cursorSensitivity, point) == null)
			{
				Point[] polygonPoints = GetPolygonAnchorPoints(chartControl, chartScale, true);

				if (polygonPoints.Length > 0 && (polygonPoints.Last() - point).Length <= cursorSensitivity)
					return IsLocked ? Cursors.Arrow : Cursors.SizeAll;

				for (int i = 0; i < ChartAnchors.Count; i++)
				{
					Point p = ChartAnchors[i].GetPoint(chartControl, chartPanel, chartScale);

					if (i + 1 < ChartAnchors.Count)
					{
						Point p2 = ChartAnchors[i + 1].GetPoint(chartControl, chartPanel, chartScale);
						if (MathHelper.IsPointAlongVector(point, p, p2 - p, cursorSensitivity))
							return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
					}
					else
					{
						Point sp = ChartAnchors[0].GetPoint(chartControl, chartPanel, chartScale);

						if (MathHelper.IsPointAlongVector(point, sp, p - sp, cursorSensitivity))
							return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
					}
				}
				return null;
			}
			return IsLocked ? null : Cursors.SizeNESW;
		}

		private Point[] GetPolygonAnchorPoints(ChartControl chartControl, ChartScale chartScale, bool includeCentroid)
		{
			ChartPanel chartPanel = chartControl.ChartPanels[PanelIndex];

			if (includeCentroid)
				return GetCentroid(chartControl, chartPanel, chartScale);
			else
			{
				Point[] points = new Point[ChartAnchors.Count];
				for (int i = 0; i < points.Count(); i++)
					points[i] = ChartAnchors[i].GetPoint(chartControl, chartPanel, chartScale);
				return points;
			}
		}

		private Point[] GetCentroid(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale)
		{
			double accumulatedArea	= 0.0;
			double centerX			= 0.0;
			double centerY			= 0.0;
			Point centroid			= new Point();

			for (int i = 0, j = ChartAnchors.Count - 1; i < ChartAnchors.Count; j = i++)
			{
				Point p			= ChartAnchors[i].GetPoint(chartControl, chartPanel, chartScale);
				Point p2		= ChartAnchors[j].GetPoint(chartControl, chartPanel, chartScale);
				double temp		= p.X * p2.Y - p2.X * p.Y;
				accumulatedArea += temp;
				centerX			+= (p.X + p2.X) * temp;
				centerY			+= (p.Y + p2.Y) * temp;
			}

			if (Math.Abs(accumulatedArea) < 1E-7f)
				return GetPolygonAnchorPoints(chartControl, chartScale, false);
			else
			{
				accumulatedArea *= 3f;
				centroid.X		= centerX / accumulatedArea;
				centroid.Y		= centerY / accumulatedArea;

				if (!IsPointInsidePolygon(chartControl, chartPanel, chartScale, centroid))
					return GetPolygonAnchorPoints(chartControl, chartScale, false);

				Point[] points = new Point[ChartAnchors.Count + 1];

				for (int i = 0; i < points.Count(); i++)
					if (i < ChartAnchors.Count)
						points[i] = ChartAnchors[i].GetPoint(chartControl, chartPanel, chartScale);

				points[points.Count() - 1] = centroid;

				return points;
			}
		}

		public override IEnumerable<Condition> GetValidAlertConditions()
		{
			return new[] { Condition.CrossInside, Condition.CrossOutside };
		}

		public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			return GetPolygonAnchorPoints(chartControl, chartScale, DrawingState != DrawingState.Building);
		}

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values, ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel chartPanel						= chartControl.ChartPanels[PanelIndex];
			Func<ChartAlertValue, Point> getBarPoint	= v => v.ValueType != ChartAlertValueType.StaticValue ? new Point(chartControl.GetXByTime(v.Time), chartScale.GetYByValue(v.Value)) : new Point(0, 0);
			Predicate<ChartAlertValue> predicate		= v =>
			{
				bool isInside = IsPointInsidePolygon(chartControl, chartPanel, chartScale, getBarPoint(v));
				return condition == Condition.CrossInside ? isInside : !isInside;
			};

			return MathHelper.DidPredicateCross(values, predicate);
		}

		private bool IsPointInsidePolygon(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point p)
		{
			bool returnVal = false;

			for (int i = 0, j = ChartAnchors.Count - 1; i < ChartAnchors.Count; j = i++)
			{
				Point p1 = ChartAnchors[i].GetPoint(chartControl, chartPanel, chartScale);
				Point p2 = ChartAnchors[j].GetPoint(chartControl, chartPanel, chartScale);

				if (((p1.Y > p.Y) != (p2.Y > p.Y)) && (p.X < (p2.X - p1.X) * (p.Y - p1.Y) / (p2.Y - p1.Y) + p1.X))
					returnVal = !returnVal;
			}

			return returnVal;
		}

		public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (DrawingState == DrawingState.Building)
				return true;

			float minX				= float.MaxValue;
			float maxX				= float.MinValue;
			ChartPanel chartPanel	= chartControl.ChartPanels[PanelIndex];

			foreach (Point pt in ChartAnchors.Select(a => a.GetPoint(chartControl, chartPanel, chartScale)))
			{
				minX = (float)Math.Min(minX, pt.X);
				maxX = (float)Math.Max(maxX, pt.X);
			}

			DateTime leftWidthTime	= chartControl.GetTimeByX((int)minX);
			DateTime rightWidthTime = chartControl.GetTimeByX((int)maxX);

			return leftWidthTime <= lastTimeOnChart && rightWidthTime >= firstTimeOnChart;
		}

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;

			if (!IsVisible)
				return;

			if (ChartAnchors.Any(a => !a.IsEditing))
			{
				foreach (ChartAnchor ca in ChartAnchors)
				{
					MinValue = Math.Min(MinValue, ca.Price);
					MaxValue = Math.Max(MaxValue, ca.Price);
				}
			}
		}

		public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			Point p = dataPoint.GetPoint(chartControl, chartPanel, chartScale);

			switch (DrawingState)
			{
				case DrawingState.Building:
					if (ChartAnchors.Count == 0)
						ChartAnchors.Add(new ChartAnchor { DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchor, IsEditing = true, DrawingTool = this });

					foreach (ChartAnchor ca in ChartAnchors)
					{
						if (ca.IsEditing)
						{
							dataPoint.CopyDataValues(ca);
							ca.IsEditing = false;
						}
					}

					if (ChartAnchors.Count > 2 && (GetCursor(chartControl, chartPanel, chartScale, p) == Cursors.Cross || DateTime.Now.Subtract(lastClick).TotalMilliseconds <= 200))
					{
						ChartAnchors.Remove(ChartAnchors[ChartAnchors.Count - 1]);
						DrawingState	= DrawingState.Normal;
						IsSelected		= false;
					}
					else
					{
						ChartAnchor ca = new ChartAnchor { DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchor, IsEditing = true, DrawingTool = this };
						dataPoint.CopyDataValues(ca);
						ChartAnchors.Add(ca);
					}

                    lastClick = DateTime.Now;
					break;
				case DrawingState.Normal:
					editingAnchor = GetClosestAnchor(chartControl, chartPanel, chartScale, cursorSensitivity, p);
					if (editingAnchor != null)
					{
						editingAnchor.IsEditing = true;
						DrawingState = DrawingState.Editing;
					}
					else
					{
						if (GetCursor(chartControl, chartPanel, chartScale, p) != null)
							DrawingState = DrawingState.Moving;
						else if (!IsPointInsidePolygon(chartControl, chartPanel, chartScale, p))
							IsSelected = false;
					}
					break;
			}
		}

		public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (IsLocked && DrawingState != DrawingState.Building)
				return;

			switch (DrawingState)
			{
				case DrawingState.Building:
					foreach (ChartAnchor ca in ChartAnchors)
						if (ca.IsEditing)
							dataPoint.CopyDataValues(ca);
					break;
				case DrawingState.Editing:
					if (editingAnchor != null)
						dataPoint.CopyDataValues(editingAnchor);
					break;
				case DrawingState.Moving:
					foreach (ChartAnchor ca in ChartAnchors)
						ca.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
					break;
			}
		}

		public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (DrawingState == DrawingState.Building)
				return;

			if (editingAnchor != null)
			{
				editingAnchor.IsEditing = false;
				editingAnchor 			= null;
			}
			
			DrawingState = DrawingState.Normal;
		}

		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (firstTime && DrawingState == DrawingState.Normal)
			{
				firstTime = false;
				Cbi.License.Log("Polygon");
			}

			RenderTarget.AntialiasMode	= SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			Stroke outlineStroke		= OutlineStroke;
			outlineStroke.RenderTarget	= RenderTarget;
			ChartPanel chartPanel		= chartControl.ChartPanels[PanelIndex];

			// dont bother with an area brush if we're doing a hit test (software) render pass. we do not render area then.
			// this allows us to select something behind our area brush (like NT7)
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
				areaBrushDevice.RenderTarget	= null;
				areaBrushDevice.Brush			= null;
			}

			// align to full pixel to avoid unneeded aliasing
			double strokePixAdjust = outlineStroke.Width % 2 == 0 ? 0.5d : 0d;

			// always re-create polygon geometry here
			SharpDX.Direct2D1.PathGeometry polyGeo = CreatePolygonGeometry(chartControl, chartPanel, chartScale, strokePixAdjust);

			if (DrawingState != DrawingState.Building)
			{
				if (!IsInHitTest && areaBrushDevice.BrushDX != null)
					RenderTarget.FillGeometry(polyGeo, areaBrushDevice.BrushDX);
				else
				{
					// Polygon can be selected by center anchor point still, so give something for the hit test pass to 
					// detect we want to be hit test there, so draw a rect in center. actual brush doesnt matter
					Point[] points = GetPolygonAnchorPoints(chartControl, chartScale, true);
					Point centroid = points.Length > 0 ? points.Last() : new Point();
					RenderTarget.FillRectangle(new SharpDX.RectangleF((float)centroid.X - 5f, (float)centroid.Y - 5f, (float)cursorSensitivity, (float)cursorSensitivity), chartControl.SelectionBrush);
				}
			}

			SharpDX.Direct2D1.Brush tmpBrush = IsInHitTest ? chartControl.SelectionBrush : outlineStroke.BrushDX;
			RenderTarget.DrawGeometry(polyGeo, tmpBrush, outlineStroke.Width, outlineStroke.StrokeStyle);
			polyGeo.Dispose();
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				AreaBrush		= Brushes.CornflowerBlue;
				AreaOpacity		= 40;
				DrawingState	= DrawingState.Building;
				Name			= Custom.Resource.NinjaScriptDrawingToolPolygon;
				OutlineStroke	= new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Solid, 2, 100);
				ChartAnchors 	= new List<ChartAnchor>();
			}
			else if (State == State.Terminated)
				Dispose();
		}
	}
	public static partial class Draw
	{
		private static Polygon PolygonCore(NinjaScriptBase owner, string tag, bool isAutoScale, List<ChartAnchor> chartAnchors, Brush brush,
			DashStyleHelper dashStyle, Brush areaBrush, int areaOpacity, bool isGlobal, string templateName)
		{
			if (owner == null)
				throw new ArgumentException("owner");

			if (string.IsNullOrWhiteSpace(tag))
				throw new ArgumentException(@"tag cant be null or empty", "tag");

			if (isGlobal && tag[0] != GlobalDrawingToolManager.GlobalDrawingToolTagPrefix)
				tag = GlobalDrawingToolManager.GlobalDrawingToolTagPrefix + tag;

			Polygon polygon = DrawingTool.GetByTagOrNew(owner, typeof(Polygon), tag, templateName) as Polygon;

			if (polygon == null)
				return null;

			DrawingTool.SetDrawingToolCommonValues(polygon, tag, isAutoScale, owner, isGlobal);

			if (chartAnchors != null)
				polygon.ChartAnchors = chartAnchors;

			if (brush != null)
				polygon.OutlineStroke = new Stroke(brush, dashStyle, 2);

			if (areaBrush != null)
				polygon.AreaBrush = areaBrush;

			if (areaOpacity > -1)
				polygon.AreaOpacity = areaOpacity;

			polygon.SetState(State.Active);
			return polygon;
		}

		private static Polygon PolygonBasic(NinjaScriptBase owner, string tag, bool isAutoScale,
			int anchor1BarsAgo, DateTime anchor1Time, double anchor1Y, int anchor2BarsAgo, DateTime anchor2Time, double anchor2Y,
			int anchor3BarsAgo, DateTime anchor3Time, double anchor3Y, int anchor4BarsAgo, DateTime anchor4Time, double anchor4Y,
			int anchor5BarsAgo, DateTime anchor5Time, double anchor5Y, int anchor6BarsAgo, DateTime anchor6Time, double anchor6Y)
		{
			List<ChartAnchor> chartAnchors = new List<ChartAnchor>();

			chartAnchors.Add(DrawingTool.CreateChartAnchor(owner, anchor1BarsAgo, anchor1Time, anchor1Y));
			chartAnchors.Add(DrawingTool.CreateChartAnchor(owner, anchor2BarsAgo, anchor2Time, anchor2Y));
			chartAnchors.Add(DrawingTool.CreateChartAnchor(owner, anchor3BarsAgo, anchor3Time, anchor3Y));
			chartAnchors.Add(DrawingTool.CreateChartAnchor(owner, anchor4BarsAgo, anchor4Time, anchor4Y));

			if (anchor5BarsAgo != int.MinValue || anchor5Time != DateTime.MinValue)
				chartAnchors.Add(DrawingTool.CreateChartAnchor(owner, anchor5BarsAgo, anchor5Time, anchor5Y));

			if (anchor6BarsAgo != int.MinValue || anchor6Time != DateTime.MinValue)
				chartAnchors.Add(DrawingTool.CreateChartAnchor(owner, anchor6BarsAgo, anchor6Time, anchor6Y));

			return PolygonCore(owner, tag, isAutoScale, chartAnchors, null, DashStyleHelper.Solid, null, int.MinValue, false, string.Empty);
		}

		/// <summary>
		/// Draws a Polygon.
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
		/// <param name="anchor4BarsAgo">The number of bars ago (x axis coordinate) to draw the fourth anchor point</param>
		/// <param name="anchor4Y">The y value coordinate of the fourth anchor point</param>
		/// <returns></returns>
		public static Polygon Polygon(NinjaScriptBase owner, string tag, bool isAutoScale, int anchor1BarsAgo, double anchor1Y, int anchor2BarsAgo, double anchor2Y, int anchor3BarsAgo, double anchor3Y, int anchor4BarsAgo, double anchor4Y)
		{
			return PolygonBasic(owner, tag, isAutoScale, anchor1BarsAgo, DateTime.MinValue, anchor1Y, anchor2BarsAgo, DateTime.MinValue, anchor2Y, anchor3BarsAgo, DateTime.MinValue, anchor3Y, anchor4BarsAgo, DateTime.MinValue, anchor4Y, int.MinValue, DateTime.MinValue, double.MinValue, int.MinValue, DateTime.MinValue, double.MinValue);
		}

		/// <summary>
		/// Draws a Polygon.
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
		/// <param name="anchor4Time">The time at which to draw the fourth anchor point</param>
		/// <param name="anchor4Y">The y value coordinate of the fourth anchor point</param>
		/// <returns></returns>
		public static Polygon Polygon(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime anchor1Time, double anchor1Y, DateTime anchor2Time, double anchor2Y, DateTime anchor3Time, double anchor3Y, DateTime anchor4Time, double anchor4Y)
		{
			return PolygonBasic(owner, tag, isAutoScale, int.MinValue, anchor1Time, anchor1Y, int.MinValue, anchor2Time, anchor2Y, int.MinValue, anchor3Time, anchor3Y, int.MinValue, anchor4Time, anchor4Y, int.MinValue, DateTime.MinValue, double.MinValue, int.MinValue, DateTime.MinValue, double.MinValue);
		}

		/// <summary>
		/// Draws a Polygon.
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
		/// <param name="anchor4BarsAgo">The number of bars ago (x axis coordinate) to draw the fourth anchor point</param>
		/// <param name="anchor4Y">The y value coordinate of the fourth anchor point</param>
		/// <param name="anchor5BarsAgo">The number of bars ago (x axis coordinate) to draw the fifth anchor point</param>
		/// <param name="anchor5Y">The y value coordinate of the fifth anchor point</param>
		/// <returns></returns>
		public static Polygon Polygon(NinjaScriptBase owner, string tag, bool isAutoScale, int anchor1BarsAgo, double anchor1Y, int anchor2BarsAgo, double anchor2Y, int anchor3BarsAgo, double anchor3Y, int anchor4BarsAgo, double anchor4Y, int anchor5BarsAgo, double anchor5Y)
		{
			return PolygonBasic(owner, tag, isAutoScale, anchor1BarsAgo, DateTime.MinValue, anchor1Y, anchor2BarsAgo, DateTime.MinValue, anchor2Y, anchor3BarsAgo, DateTime.MinValue, anchor3Y, anchor4BarsAgo, DateTime.MinValue, anchor4Y, anchor5BarsAgo, DateTime.MinValue, anchor5Y, int.MinValue, DateTime.MinValue, int.MinValue);
		}

		/// <summary>
		/// Draws a Polygon.
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
		/// <param name="anchor4Time">The time at which to draw the fourth anchor point</param>
		/// <param name="anchor4Y">The y value coordinate of the fourth anchor point</param>
		/// <param name="anchor5Time">The time at which to draw the fifth anchor point</param>
		/// <param name="anchor5Y">The y value coordinate of the fifth anchor point</param>
		/// <returns></returns>
		public static Polygon Polygon(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime anchor1Time, double anchor1Y, DateTime anchor2Time, double anchor2Y, DateTime anchor3Time, double anchor3Y, DateTime anchor4Time, double anchor4Y, DateTime anchor5Time, double anchor5Y)
		{
			return PolygonBasic(owner, tag, isAutoScale, int.MinValue, anchor1Time, anchor1Y, int.MinValue, anchor2Time, anchor2Y, int.MinValue, anchor3Time, anchor3Y, int.MinValue, anchor4Time, anchor4Y, int.MinValue, anchor5Time, anchor5Y, int.MinValue, DateTime.MinValue, double.MinValue);
		}

		/// <summary>
		/// Draws a Polygon.
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
		/// <param name="anchor4BarsAgo">The number of bars ago (x axis coordinate) to draw the fourth anchor point</param>
		/// <param name="anchor4Y">The y value coordinate of the fourth anchor point</param>
		/// <param name="anchor5BarsAgo">The number of bars ago (x axis coordinate) to draw the fifth anchor point</param>
		/// <param name="anchor5Y">The y value coordinate of the fifth anchor point</param>
		/// <param name="anchor6BarsAgo">The number of bars ago (x axis coordinate) to draw the sixth anchor point</param>
		/// <param name="anchor6Y">The y value coordinate of the sixth anchor point</param>
		/// <returns></returns>
		public static Polygon Polygon(NinjaScriptBase owner, string tag, bool isAutoScale, int anchor1BarsAgo, double anchor1Y, int anchor2BarsAgo, double anchor2Y, int anchor3BarsAgo, double anchor3Y, int anchor4BarsAgo, double anchor4Y, int anchor5BarsAgo, double anchor5Y, int anchor6BarsAgo, double anchor6Y)
		{
			return PolygonBasic(owner, tag, isAutoScale, anchor1BarsAgo, DateTime.MinValue, anchor1Y, anchor2BarsAgo, DateTime.MinValue, anchor2Y, anchor3BarsAgo, DateTime.MinValue, anchor3Y, anchor4BarsAgo, DateTime.MinValue, anchor4Y, anchor5BarsAgo, DateTime.MinValue, anchor5Y, anchor6BarsAgo, DateTime.MinValue, anchor6Y);
		}

		/// <summary>
		/// Draws a Polygon.
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
		/// <param name="anchor4Time">The time at which to draw the fourth anchor point</param>
		/// <param name="anchor4Y">The y value coordinate of the fourth anchor point</param>
		/// <param name="anchor5Time">The time at which to draw the fifth anchor point</param>
		/// <param name="anchor5Y">The y value coordinate of the fifth anchor point</param>
		/// <param name="anchor6Time">The time at which to draw the sixth anchor point</param>
		/// <param name="anchor6Y">The y value coordinate of the sixth anchor point</param>
		/// <returns></returns>
		public static Polygon Polygon(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime anchor1Time, double anchor1Y, DateTime anchor2Time, double anchor2Y, DateTime anchor3Time, double anchor3Y, DateTime anchor4Time, double anchor4Y, DateTime anchor5Time, double anchor5Y, DateTime anchor6Time, double anchor6Y)
		{
			return PolygonBasic(owner, tag, isAutoScale, int.MinValue, anchor1Time, anchor1Y, int.MinValue, anchor2Time, anchor2Y, int.MinValue, anchor3Time, anchor3Y, int.MinValue, anchor4Time, anchor4Y, int.MinValue, anchor5Time, anchor5Y, int.MinValue, anchor6Time, anchor6Y);
		}

		/// <summary>
		/// Draws a Polygon.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="chartAnchors">A List of ChartAnchor objects defining the polygon</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		public static Polygon Polygon(NinjaScriptBase owner, string tag, bool isAutoScale, List<ChartAnchor> chartAnchors, Brush brush, DashStyleHelper dashStyle, Brush areaBrush, int areaOpacity)
		{
			return PolygonCore(owner, tag, isAutoScale, chartAnchors, brush, dashStyle, areaBrush, areaOpacity, false, string.Empty);
		}

		/// <summary>
		/// Draws a Polygon.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="chartAnchors">A List of ChartAnchor objects defining the polygon</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static Polygon Polygon(NinjaScriptBase owner, string tag, bool isAutoScale, List<ChartAnchor> chartAnchors, bool isGlobal, string templateName)
		{
			return PolygonCore(owner, tag, isAutoScale, chartAnchors, null, DashStyleHelper.Solid, null, int.MinValue, isGlobal, templateName);
		}
	}
}
