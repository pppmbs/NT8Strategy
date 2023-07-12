// 
// Copyright (C) 2022, NinjaTrader LLC <www.ninjatrader.com>.
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

namespace NinjaTrader.NinjaScript.DrawingTools
{
	public abstract class ShapeBase : DrawingTool
	{
		// this base class is used to draw and manage all three types, so this is used to keep track 
		// of which type of shape is being manipulated. does not need to be localized because not visible on  UI
		protected enum ChartShapeType
		{
			Unset,
			Ellipse,
			Rectangle,
			Triangle
		}
		
		// for rectangle and ellipse, we show a few fake anchors along edges/corners, so we need to
		// figure out how to actually size the 2 data anchors
		protected enum ResizeMode 
		{
			None,
			TopLeft,
			TopRight,
			BottomLeft,
			BottomRight,
			MoveAll,
		}
		
		private				int							areaOpacity;
		private				Brush						areaBrush;
		private	readonly	DeviceBrush					areaBrushDevice			= new DeviceBrush();
		private	const		double						cursorSensitivity		= 15;
		private				ChartAnchor					editingAnchor;
		private				ChartAnchor 				editingLeftAnchor;
		private				ChartAnchor 				editingTopAnchor; 
		private				ChartAnchor 				editingBottomAnchor;
		private				ChartAnchor 				editingRightAnchor;
		private				ChartAnchor					lastMouseMoveDataPoint;
		private				ResizeMode					resizeMode;
		
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
			get { return Serialize.BrushToString(AreaBrush); }
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

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolTextOutlineStroke", GroupName = "NinjaScriptGeneral", Order = 3)]
		public Stroke				OutlineStroke	{ get; set; }

		// common to shapes
		[Display(Order = 2)]
		public ChartAnchor			EndAnchor		{ get; set;	}
		[Display(Order = 3)]
		public ChartAnchor			MiddleAnchor	{ get; set; } // only used for triangle
		[Display(Order = 1)]
		public ChartAnchor			StartAnchor		{ get; set; }

		[Browsable(false)]
		protected ChartShapeType 	ShapeType 		{ get; set;	}
		
		public override bool SupportsAlerts { get { return true; } }
		
		public override IEnumerable<ChartAnchor> Anchors { get { return ShapeType == ChartShapeType.Triangle ? new[] { StartAnchor, MiddleAnchor, EndAnchor } : new[] { StartAnchor, EndAnchor }; } }

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;

			if (!IsVisible)
				return;

			// return min/max values only if something has been actually drawn
			if (Anchors.Any(a => !a.IsEditing))
				foreach (ChartAnchor anchor in Anchors)
				{
					MinValue = Math.Min(anchor.Price, MinValue);
					MaxValue = Math.Max(anchor.Price, MaxValue);
				}
		}
		
		private SharpDX.Direct2D1.PathGeometry CreateTriangleGeometry(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, double pixelAdjust)
		{
			Point startPoint			= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point midPoint				= MiddleAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point endPoint 				= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Vector pixelAdjustVec		= new Vector(pixelAdjust, pixelAdjust);
			SharpDX.Vector2 startVec 	= (startPoint + pixelAdjustVec).ToVector2();
			SharpDX.Vector2 midVec 		= (midPoint + pixelAdjustVec).ToVector2();
			SharpDX.Vector2 endVec 		= (endPoint + pixelAdjustVec).ToVector2();
			
			SharpDX.Direct2D1.PathGeometry pathGeometry = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
			SharpDX.Direct2D1.GeometrySink geometrySink = pathGeometry.Open();
			geometrySink.BeginFigure(startVec, SharpDX.Direct2D1.FigureBegin.Filled);

			geometrySink.AddLines(new[] 
			{
				startVec, midVec, 	// start -> mid,
				midVec, endVec,		// mid -> top
				endVec, startVec 	// top -> start (cap it off)
			});
				
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

		private Rect GetAnchorsRect(ChartControl chartControl, ChartScale chartScale)
		{
			if (StartAnchor == null || EndAnchor == null)
				return new Rect();
			
			ChartPanel chartPanel	= chartControl.ChartPanels[chartScale.PanelIndex];
			Point startPoint		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point endPoint 			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			
			//rect doesnt handle negative width/height so we need to determine and wind it up ourselves
			// make sure to always use smallest left/top anchor for start
			double left 	= Math.Min(endPoint.X, startPoint.X);
			double top 		= Math.Min(endPoint.Y, startPoint.Y);
			double width 	= Math.Abs(endPoint.X - startPoint.X);
			double height 	= Math.Abs(endPoint.Y - startPoint.Y);
			return new Rect(left, top, width, height);
		}

		public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
		{
			if (DrawingState == DrawingState.Building)
				return Cursors.Pen;
			
			if (DrawingState == DrawingState.Moving)
				return IsLocked ? Cursors.No : Cursors.SizeAll;
			
			if (DrawingState == DrawingState.Editing && IsLocked)
				return Cursors.No;
			
			// work like NT7 here, if we're on the edge of a shape, or dead center, move

			if (ShapeType == ChartShapeType.Triangle)
			{
				// triangle shows actual anchor points so much easier
				ChartAnchor closest = GetClosestAnchor(chartControl, chartPanel, chartScale, cursorSensitivity, point);
				if (closest == null)
				{
					// get the centroid point, see if we're near that
					Point[] trianglePoints = GetTriangleAnchorPoints(chartControl,chartScale, true);
					if ((trianglePoints.Last() - point).Length <= cursorSensitivity)
						return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
					// check points as vectors to each other to check each edge (dont use center point which is last in the array)
					for (int i = 0; i < 3; ++i)
					{
						Point startPoint = trianglePoints[i == 2 ? 0 : i + 1];
						Vector vec = trianglePoints[i] - startPoint;
						if (MathHelper.IsPointAlongVector(point, startPoint, vec, 10))
							return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
					}

					return null;
				}
				return 	IsLocked ? null : Cursors.SizeNESW;
			}
			
			bool isRect = ShapeType == ChartShapeType.Rectangle;
			
			ResizeMode tmpResizeMode = resizeMode != ResizeMode.None ? resizeMode : GetResizeModeForPoint(point, chartControl, chartScale, DrawingState == DrawingState.Normal);
			switch (tmpResizeMode)
			{
				// ellipse rect is shifted to centerpoints of each rectangel edge,
				// so we wanna use up/down/left/right variants instead for it
				case ResizeMode.TopLeft: 		// fall through
				case ResizeMode.BottomRight:	return	IsLocked ? Cursors.Arrow : isRect ? Cursors.SizeNWSE : Cursors.SizeNS;
				case ResizeMode.TopRight: 		// fall through
				case ResizeMode.BottomLeft: 	return	IsLocked ? Cursors.Arrow : isRect ? Cursors.SizeNESW : Cursors.SizeWE;
				case ResizeMode.MoveAll:		return	IsLocked ? Cursors.Arrow : Cursors.SizeAll;
			}
			return null;
		}
		
		private static Point? GetClosestPoint(IEnumerable<Point> inputPoints, Point desired, bool useSensitivity)
		{
			IOrderedEnumerable<Point> ordered = inputPoints.OrderBy(pt => (pt - desired).Length);
			Point closestPoint = ordered.First();
			if (useSensitivity && (closestPoint - desired).Length > cursorSensitivity)
				return null;
			return closestPoint;
		}
		
		private Point[] GetEllipseAnchorPoints(ChartControl chartControl, ChartScale chartScale)
		{
			Rect rect = GetAnchorsRect(chartControl, chartScale);
			Point centerPoint = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
			return new[] 
			{
				new Point(rect.TopLeft.X + rect.Width / 2, rect.Top), 
				new Point(rect.Right, rect.TopRight.Y + rect.Height / 2),
				new Point(rect.Right - rect.Width / 2, rect.Bottom),
				new Point(rect.Left, rect.Top + rect.Height / 2),
				centerPoint
			};
		}

		private ResizeMode GetResizeModeForPoint(Point pt, ChartControl chartControl, ChartScale chartScale, bool useCursorSens)
		{
			switch (ShapeType)
			{
				case ChartShapeType.Ellipse:
				{
					Point[] ellipsePoints = GetEllipseAnchorPoints(chartControl, chartScale);
					Point centerPoint = ellipsePoints.Last();
					Point? closest = GetClosestPoint(ellipsePoints, pt, useCursorSens);
					if (closest != null)
					{
						int ptIndex;
						for (ptIndex = 0; ptIndex < ellipsePoints.Length; ++ptIndex)
						{
							if (ellipsePoints[ptIndex] == closest.Value)
								break;
						}
						// use index of closest point (within sensitivity) 
						switch (ptIndex)
						{
							case 0:		return ResizeMode.TopLeft;
							case 1: 	return ResizeMode.TopRight;
							case 2: 	return ResizeMode.BottomRight;
							case 3: 	return ResizeMode.BottomLeft;
						}
					}
					
					if ((centerPoint - pt).Length < cursorSensitivity)
						return ResizeMode.MoveAll;
					// user didnt click anchor or center, create diamond vectors along visible anchor points and use that for approx move selection
					// check if mouse is along edges of rect, and do move if so
					for (int i = 0; i < 4; ++i)
					{
						Point startPoint = ellipsePoints[i == 3 ? 0 : i + 1]; // if we're on last point, check to first
						Vector vec = ellipsePoints[i] - startPoint;
						if (MathHelper.IsPointAlongVector(pt, startPoint, vec, 25))
							return ResizeMode.MoveAll;
					}
					break;
				}
				case ChartShapeType.Rectangle:
				{
					Rect rect = GetAnchorsRect(chartControl, chartScale);
					Point[] rectPoints = { rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft }; // wind clockwise for  vector check below
					Point? closest = GetClosestPoint(rectPoints, pt, useCursorSens);
					if (closest != null)
					{
						int ptIndex;
						for (ptIndex = 0; ptIndex < rectPoints.Length; ++ptIndex)
						{
							if (rectPoints[ptIndex] == closest.Value)
								break;
						}
						// use index of closest point (within sensitivity) 
						switch (ptIndex)
						{
							case 0: return ResizeMode.TopLeft;
							case 1: return ResizeMode.TopRight;
							case 2: return ResizeMode.BottomRight;
							case 3: return ResizeMode.BottomLeft;
							default: return ResizeMode.MoveAll; // center point
						}
					}
					// check if mouse is along edges of rect, and do move if so
					for (int i = 0; i < 4; ++i)
					{
						Point startPoint = rectPoints[i == 3 ? 0 : i + 1]; // if we're on last point, check to first
						Vector vec = rectPoints[i] - startPoint;
						if (MathHelper.IsPointAlongVector(pt, startPoint, vec, cursorSensitivity))
							return ResizeMode.MoveAll;
					}
					break;
				}
			}
			return ResizeMode.None;
		}
	
		public sealed override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			switch (ShapeType)
			{
				case ChartShapeType.Ellipse:
					return GetEllipseAnchorPoints(chartControl, chartScale);
				case ChartShapeType.Rectangle:
					Rect rect = GetAnchorsRect(chartControl, chartScale);
					return new[]
					{
						rect.TopLeft, rect.TopRight,
						rect.BottomLeft, rect.BottomRight
					};
				case ChartShapeType.Triangle:
					return GetTriangleAnchorPoints(chartControl, chartScale, true);
			}
			return new Point[0];
		}
		
		private Point[] GetTriangleAnchorPoints(ChartControl chartControl, ChartScale chartScale, bool includeCentroid)
		{
			ChartPanel	chartPanel	= chartControl.ChartPanels[PanelIndex];
			Point		midPoint 	= MiddleAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point		startPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point		endPoint	= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);

			return includeCentroid
				? new[] { startPoint, midPoint, endPoint, new Point((startPoint.X + midPoint.X + endPoint.X)/3d, (startPoint.Y + midPoint.Y + endPoint.Y)/3d) }
				: new[] { startPoint, midPoint, endPoint };
		}
	
		public override IEnumerable<AlertConditionItem> GetAlertConditionItems()
		{
			// nothing fancy here, but we do need to return at least 1 condition item to check an
			// alert on to be considered valid
			yield return new AlertConditionItem 
			{
				Name					= "Shape area",
				ShouldOnlyDisplayName	= true,
			};
		}
		
		public override IEnumerable<Condition> GetValidAlertConditions()
		{
			return new[] { Condition.CrossInside, Condition.CrossOutside };
		}
		
		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values, ChartControl chartControl, ChartScale chartScale)
		{
			// first make sure the values fall in our chart object at all
			double		minPrice	= Anchors.Min(a => a.Price);
			double		maxPrice	= Anchors.Max(a => a.Price);
			DateTime	minTime		= Anchors.Min(a => a.Time);
			DateTime	maxTime		= Anchors.Max(a => a.Time);

			ChartPanel	chartPanel	= chartControl.ChartPanels[PanelIndex];
			Point		startPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point		endPoint	= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point		centerPoint	= startPoint + ((endPoint - startPoint) * 0.5);
				
			Predicate<ChartAlertValue>	predicate;
			Func<ChartAlertValue,Point> getBarPoint = v => v.ValueType != ChartAlertValueType.StaticValue ? 
				new Point(chartControl.GetXByTime(v.Time),chartScale.GetYByValue(v.Value)) : new Point(0,0); 

			switch (ShapeType)
			{
				case ChartShapeType.Rectangle: 
					// for rectangle simply check we went into/out the rectangle bounds
					predicate = v =>
					{
						bool isInside = v.Value >= minPrice && v.Value <= maxPrice && v.Time >= minTime && v.Time <= maxTime;
						return condition == Condition.CrossInside ? isInside : !isInside;
					};
					break;
				case ChartShapeType.Ellipse:
					// sign doesnt matter here for ellipse axis w/h (centered on origin)
					double a = Math.Abs(endPoint.X - startPoint.X) / 2d;
					double b = Math.Abs(endPoint.Y - startPoint.Y) / 2d;
					predicate = v =>
					{
						bool isInside = MathHelper.IsPointInsideEllipse(centerPoint, getBarPoint(v), a, b);
						return condition == Condition.CrossInside ? isInside : !isInside; 
					};
					break;
				case ChartShapeType.Triangle:
					Point[] trianglePoints = GetTriangleAnchorPoints(chartControl, chartScale, false);
					predicate = v =>
					{
						bool isInside = MathHelper.IsPointInsideTriangle(getBarPoint(v), trianglePoints[0], trianglePoints[1], trianglePoints[2]);
						return condition == Condition.CrossInside ? isInside : !isInside;
					};
					break;
				default: return false;
			}
		
			return MathHelper.DidPredicateCross(values, predicate);
		}
		
		public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (DrawingState == DrawingState.Building)
				return true;

			float minX = float.MaxValue;
			float maxX = float.MinValue;
			ChartPanel chartPanel = chartControl.ChartPanels[PanelIndex];
			// create an axis aligned bounding box taking into acct all 3 points for triangle to use for visibility checks
			foreach (Point pt in Anchors.Select(a => a.GetPoint(chartControl, chartPanel, chartScale)))
			{
				minX = (float)Math.Min(minX, pt.X);
				maxX = (float)Math.Max(maxX, pt.X);
			}

			DateTime	leftWidthTime	= chartControl.GetTimeByX((int) minX);
			DateTime	rightWidthTime	= chartControl.GetTimeByX((int) maxX);
			
			// check our width is visible somewhere horizontally
			return leftWidthTime <= lastTimeOnChart && rightWidthTime >= firstTimeOnChart;
		}

		public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (ShapeType == ChartShapeType.Unset)
				return;

			switch (DrawingState)
			{
				case DrawingState.Building:
					if (StartAnchor.IsEditing)
					{
						// give mid & end anchor something to start with so we dont try to render it with bad values right away
						dataPoint.CopyDataValues(StartAnchor);
						dataPoint.CopyDataValues(MiddleAnchor);
						dataPoint.CopyDataValues(EndAnchor);
						StartAnchor.IsEditing = false;
					}
					else if (ShapeType == ChartShapeType.Triangle && MiddleAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(MiddleAnchor);
						MiddleAnchor.IsEditing = false;
					}
					else if (EndAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(EndAnchor);
						EndAnchor.IsEditing = false;
					}
					
					// is initial building done? (all anchors set)
					if (!StartAnchor.IsEditing && !EndAnchor.IsEditing)
					{
						// if we're a triangle, check middle is done too
						if (ShapeType != ChartShapeType.Triangle || !MiddleAnchor.IsEditing)
						{
							DrawingState 	= DrawingState.Normal;
							IsSelected 		= false; 
						}
					}
					break;
				case DrawingState.Normal:
					Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
					switch (ShapeType)
					{
						case ChartShapeType.Triangle:
							editingAnchor = GetClosestAnchor(chartControl, chartPanel, chartScale, cursorSensitivity, point);
							if (editingAnchor != null)
							{
								editingAnchor.IsEditing = true;
								DrawingState = DrawingState.Editing;
							}
							else
							{
								if (GetCursor(chartControl, chartPanel, chartScale, point) != null)
									DrawingState = DrawingState.Moving;
								else
								{
									Point[] trianglePoints = GetTriangleAnchorPoints(chartControl,chartScale, true);
									if (!MathHelper.IsPointInsideTriangle(point, trianglePoints[0], trianglePoints[1], trianglePoints[2]))
										IsSelected = false;
								}
							}
							break;
						case ChartShapeType.Ellipse:
						case ChartShapeType.Rectangle:
						{
							// rect / ellipse mode, which has 4 anchor points shown but only 2 actual anchor points.
							// depending on which one is being edited, may have to update a few anchors specific axis
							// however dont use UpdateX/YFromPoint() directly on the point, we want it to be relative
							// furthermore, we cant assume StartAnchor == top left and EndAnchor == bottom right
							// for anchor rectangle. Reason is if user draws it backwards, so we need to determine
							// which anchor currently corresponds to topleft / bottomright
							// we only grab these once at start of edit and save during edit. trying to update
							// during edit would cause them to change, making the rect wiggle around instead of resize
							// when trying to resize through an edge
							Point startPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
							Point endPoint		= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
							editingLeftAnchor	= startPoint.X <= endPoint.X ? StartAnchor : EndAnchor;
							editingTopAnchor	= startPoint.Y <= endPoint.Y ? StartAnchor : EndAnchor;
							editingBottomAnchor	= startPoint.Y <= endPoint.Y ? EndAnchor : StartAnchor;
							editingRightAnchor	= startPoint.X <= endPoint.X ? EndAnchor : StartAnchor;

							// NOTE: This may actually return 'no' when clicking on an anchor if its locked, which
							// would set it to moving, but that's ok because it wont actually affect the object
							Cursor clickedCursor = GetCursor(chartControl, chartPanel, chartScale, point);
							if (clickedCursor == Cursors.SizeAll || clickedCursor == Cursors.No)
								DrawingState = DrawingState.Moving;
							else
							{
								// we need to emulate editing depending on where they clicked
								resizeMode = GetResizeModeForPoint(point, chartControl, chartScale, true);
								if (resizeMode != ResizeMode.None)
									DrawingState = resizeMode == ResizeMode.MoveAll ? DrawingState.Moving : DrawingState.Editing;
								else
								{
									Rect rect = GetAnchorsRect(chartControl, chartScale);
									if (!rect.IntersectsWith(new Rect(point.X, point.Y, 1, 1)))
									{
										// user missed completely, deselect
										IsSelected = false;
									}
									// otherwise they clicked in a rect, but not close to anything so dont do anything
								}
							}
							break;
						}
					}
					if (lastMouseMoveDataPoint == null)
						lastMouseMoveDataPoint = new ChartAnchor();
					dataPoint.CopyDataValues(lastMouseMoveDataPoint);
					break;
			}
		}
		
		public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (ShapeType == ChartShapeType.Unset || IsLocked && DrawingState != DrawingState.Building)
				return;
			
			if (DrawingState == DrawingState.Building)
			{
				// just simply update both these anchors as user moves mouse, end being same as middle is ok for triangle
				if (MiddleAnchor.IsEditing)
					dataPoint.CopyDataValues(MiddleAnchor);
				if (EndAnchor.IsEditing)
					dataPoint.CopyDataValues(EndAnchor);
			}
			else if (DrawingState == DrawingState.Editing)
			{
				// triangle mode: editing actual anchor, easy enough
				if (ShapeType == ChartShapeType.Triangle && editingAnchor != null)
					dataPoint.CopyDataValues(editingAnchor);
				else
				{
					if (lastMouseMoveDataPoint == null)
						lastMouseMoveDataPoint = new ChartAnchor();
					switch (resizeMode)
					{
						case ResizeMode.TopLeft:
							editingTopAnchor.Price = lastMouseMoveDataPoint.Price;
							// only update Y on ellipse because this is actually the top edge anchor
							if (ShapeType != ChartShapeType.Ellipse)
							{
								editingLeftAnchor.SlotIndex = lastMouseMoveDataPoint.SlotIndex;
								editingLeftAnchor.Time = lastMouseMoveDataPoint.Time;

								dataPoint.CopyDataValues(lastMouseMoveDataPoint);
							}
							else
							{
								// dont update X values
								lastMouseMoveDataPoint.Price = dataPoint.Price;
							}
							break;
						case ResizeMode.BottomRight: 
							// only update Y on ellipse because this is actually the bottom edge anchor
							editingBottomAnchor.Price = lastMouseMoveDataPoint.Price;
							if (ShapeType != ChartShapeType.Ellipse)
							{
								editingRightAnchor.Time = lastMouseMoveDataPoint.Time;
								editingRightAnchor.SlotIndex = lastMouseMoveDataPoint.SlotIndex;
								dataPoint.CopyDataValues(lastMouseMoveDataPoint);
							}
							else
							{
								// dont update X values
								lastMouseMoveDataPoint.Price = dataPoint.Price;
							}
							break;
						case ResizeMode.TopRight:
							// on ellipse top right is actually right edge, so dont move top up/down
							editingRightAnchor.SlotIndex = lastMouseMoveDataPoint.SlotIndex;
							editingRightAnchor.Time = lastMouseMoveDataPoint.Time;
							if (ShapeType != ChartShapeType.Ellipse)
							{
								editingTopAnchor.Price = lastMouseMoveDataPoint.Price;
								dataPoint.CopyDataValues(lastMouseMoveDataPoint);
							}
							else
							{
								// dont update price
								lastMouseMoveDataPoint.Time = dataPoint.Time;
								lastMouseMoveDataPoint.SlotIndex = dataPoint.SlotIndex;
							}
							break;
						case ResizeMode.BottomLeft:
							// no real anchor to simply move, figure out which is currently 'left' anchor
							editingLeftAnchor.Time = lastMouseMoveDataPoint.Time;
							editingLeftAnchor.SlotIndex = lastMouseMoveDataPoint.SlotIndex;
							if (ShapeType != ChartShapeType.Ellipse)
							{
								editingBottomAnchor.Price = lastMouseMoveDataPoint.Price;
								dataPoint.CopyDataValues(lastMouseMoveDataPoint);
							}
							else
							{
								// dont update price
								lastMouseMoveDataPoint.Time = dataPoint.Time;
								lastMouseMoveDataPoint.SlotIndex = dataPoint.SlotIndex;
							}
							break;
					}
				}
			}
			else if (DrawingState == DrawingState.Moving)
			{
				foreach (ChartAnchor anchor in Anchors)
					anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
			}
		}
		
		public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (DrawingState == DrawingState.Building) 
				return;

			lastMouseMoveDataPoint	= null;
			DrawingState			= DrawingState.Normal;
			editingAnchor			= null;
			editingLeftAnchor		= null;
			editingTopAnchor		= null;
			editingRightAnchor		= null;
			editingBottomAnchor		= null;
			resizeMode				= ResizeMode.None;
		}
		
		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (ShapeType == ChartShapeType.Unset)
				return;
			Stroke outlineStroke		= OutlineStroke;
			RenderTarget.AntialiasMode	= SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			outlineStroke.RenderTarget	= RenderTarget;
			ChartPanel chartPanel		= chartControl.ChartPanels[PanelIndex];
			Point startPoint			= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point endPoint				= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);

			double width				= endPoint.X - startPoint.X;
			double height				= endPoint.Y - startPoint.Y;
			
			SharpDX.Vector2 centerPoint	= (startPoint + ((endPoint - startPoint) / 2)).ToVector2();
			
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
				areaBrushDevice.RenderTarget = null;
				areaBrushDevice.Brush = null;
			}
			
			// align to full pixel to avoid unneeded aliasing
			double strokePixAdjust =	outlineStroke.Width % 2 == 0 ? 0.5d : 0d;
			
			switch (ShapeType)
			{
				case ChartShapeType.Ellipse:
				{
					SharpDX.Direct2D1.Ellipse ellipse = new SharpDX.Direct2D1.Ellipse(centerPoint,	(float)(width/2f + strokePixAdjust),
																									(float)(height/2f + strokePixAdjust));
					if (!IsInHitTest && areaBrushDevice.BrushDX != null)
						RenderTarget.FillEllipse(ellipse, areaBrushDevice.BrushDX);
					else 
					{
						// ellipse can be selected by center anchor point still, so give something for the hit test pass to 
						// detect we want to be hit test there, so draw a rect in center. actual brush doesnt matter
						RenderTarget.FillRectangle(new SharpDX.RectangleF(centerPoint.X - 5f, centerPoint.Y - 5f, (float)cursorSensitivity, (float)cursorSensitivity), chartControl.SelectionBrush);
					}
					SharpDX.Direct2D1.Brush tmpBrush = IsInHitTest ? chartControl.SelectionBrush : outlineStroke.BrushDX;
					RenderTarget.DrawEllipse(ellipse, tmpBrush, outlineStroke.Width, outlineStroke.StrokeStyle);
					break;
				}
				case ChartShapeType.Rectangle:
				{
					SharpDX.RectangleF rect = new SharpDX.RectangleF((float)(startPoint.X + strokePixAdjust), 
																	(float)(startPoint.Y + strokePixAdjust), 
																	(float)(width/* + strokePixAdjust*/), (float)(height/* + strokePixAdjust*/));
					
					if (!IsInHitTest && areaBrushDevice.BrushDX != null)
						RenderTarget.FillRectangle(rect, areaBrushDevice.BrushDX);

					SharpDX.Direct2D1.Brush tmpBrush = IsInHitTest ? chartControl.SelectionBrush : outlineStroke.BrushDX;
					RenderTarget.DrawRectangle(rect, tmpBrush, outlineStroke.Width, outlineStroke.StrokeStyle);
					break;
				}
				case ChartShapeType.Triangle:
				{
					// always re-create triangle geo here
					SharpDX.Direct2D1.PathGeometry triGeo = CreateTriangleGeometry(chartControl, chartPanel, chartScale, strokePixAdjust);
					if (!IsInHitTest && areaBrushDevice.BrushDX != null)
						RenderTarget.FillGeometry(triGeo, areaBrushDevice.BrushDX);
					else 
					{
						// Triangle can be selected by center anchor point still, so give something for the hit test pass to 
						// detect we want to be hit test there, so draw a rect in center. actual brush doesnt matter
						Point centroid = GetTriangleAnchorPoints(chartControl, chartScale, true).Last();
						RenderTarget.FillRectangle(new SharpDX.RectangleF((float)centroid.X - 5f, (float)centroid.Y - 5f, (float)cursorSensitivity, (float)cursorSensitivity), chartControl.SelectionBrush);
					}
					SharpDX.Direct2D1.Brush tmpBrush = IsInHitTest ? chartControl.SelectionBrush : outlineStroke.BrushDX;
					RenderTarget.DrawGeometry(triGeo, tmpBrush, outlineStroke.Width, outlineStroke.StrokeStyle);
					triGeo.Dispose();
					break;
				}
			}
		}
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				StartAnchor		= new ChartAnchor { DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchorStart,	IsEditing = true, DrawingTool = this };
				MiddleAnchor	= new ChartAnchor { DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchorMiddle,	IsEditing = true, DrawingTool = this };
				EndAnchor		= new ChartAnchor { DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchorEnd,		IsEditing = true, DrawingTool = this };
				DrawingState	= DrawingState.Building;
				AreaBrush		= Brushes.CornflowerBlue;
				AreaOpacity		= 40;
				OutlineStroke	= new Stroke(Brushes.CornflowerBlue, 2f);
				ShapeType		= ChartShapeType.Unset;
				MiddleAnchor.IsBrowsable = false;
			}
			else if (State == State.Terminated)
				Dispose();
		}
	}

	/// <summary>
	/// Represents an interface that exposes information regarding an Ellipse IDrawingTool.
	/// </summary>
	public class Ellipse : ShapeBase
	{
		public override object Icon { get { return Gui.Tools.Icons.DrawElipse; } }

		protected override void OnStateChange()
		{
			base.OnStateChange();
			if (State == State.SetDefaults)
			{
				Name					= Custom.Resource.NinjaScriptDrawingToolEllipse;
				ShapeType				= ChartShapeType.Ellipse;
			}
		}
	}

	/// <summary>
	/// Represents an interface that exposes information regarding a Rectangle IDrawingTool.
	/// </summary>
	public class Rectangle : ShapeBase
	{
		public override object Icon { get { return Gui.Tools.Icons.DrawRectangle; } }

		protected override void OnStateChange()
		{
			base.OnStateChange();
			if (State == State.SetDefaults)
			{
				Name		= Custom.Resource.NinjaScriptDrawingToolRectangle;
				ShapeType	= ChartShapeType.Rectangle;
			}
		}
	}

	/// <summary>
	/// Represents an interface that exposes information regarding a Triangle IDrawingTool.
	/// </summary>
	public class Triangle : ShapeBase
	{
		public override object Icon { get { return Gui.Tools.Icons.DrawTriangle; } }

		protected override void OnStateChange()
		{
			base.OnStateChange();
			if (State == State.SetDefaults)
			{
				Name		= Custom.Resource.NinjaScriptDrawingToolTriangle;
				ShapeType	= ChartShapeType.Triangle;
				MiddleAnchor.IsBrowsable = true;
			}
		}
	}

	public static partial class Draw
	{
		private static T ShapeCore<T>(NinjaScriptBase owner, bool isAutoScale, string tag, int startBarsAgo, int endBarsAgo, 
			DateTime startTime, DateTime endTime, double startY, double endY, Brush brush, Brush areaBrush, int areaOpacity, bool isGlobal, string templateName) 
			where T : ShapeBase
		{
			if (owner == null)
				throw new ArgumentException("owner");
			if (string.IsNullOrWhiteSpace(tag))
				throw new ArgumentException("tag cant be null or empty");
			if (isGlobal && tag[0] != GlobalDrawingToolManager.GlobalDrawingToolTagPrefix)
				tag = GlobalDrawingToolManager.GlobalDrawingToolTagPrefix + tag;

			T shapeT = DrawingTool.GetByTagOrNew(owner, typeof(T), tag, templateName) as T;

			if (shapeT == null)
				return null;
			if (startTime < Core.Globals.MinDate)
				throw new ArgumentException(shapeT + " startTime must be greater than the minimum Date but was " + startTime);
			else if (endTime < Core.Globals.MinDate)
				throw new ArgumentException(shapeT + " endTime must be greater than the minimum Date but was " + endTime);			

			DrawingTool.SetDrawingToolCommonValues(shapeT, tag, isAutoScale, owner, isGlobal);

			// dont overwrite existing anchor references
			ChartAnchor	startAnchor	= DrawingTool.CreateChartAnchor(owner, startBarsAgo, startTime, startY);
			ChartAnchor	endAnchor	= DrawingTool.CreateChartAnchor(owner, endBarsAgo, endTime, endY);

			startAnchor.CopyDataValues(shapeT.StartAnchor);
			endAnchor.CopyDataValues(shapeT.EndAnchor);

			// these can be null when using a templateName so mind not overwriting them
			if (brush != null)
				shapeT.OutlineStroke	= new Stroke(brush, DashStyleHelper.Solid, 2f) { RenderTarget = shapeT.OutlineStroke.RenderTarget };
			if (areaOpacity >= 0)
				shapeT.AreaOpacity		= areaOpacity;
			if (areaBrush != null)
			{
				shapeT.AreaBrush		= areaBrush.Clone();
				if (shapeT.AreaBrush.CanFreeze)
					shapeT.AreaBrush.Freeze();
			}

			shapeT.SetState(State.Active);

			return shapeT;
		}

		private static Triangle TriangleCore(NinjaScriptBase owner, bool isAutoScale, string tag,
												int startBarsAgo, int midBarsAgo, int endBarsAgo,
												DateTime startTime, DateTime midTime, DateTime endTime,
												double startY, double midY, double endY,
												Brush color, Brush areaColor, int areaOpacity, bool isGlobal, string templateName)
		{
			Triangle	triangle	= ShapeCore<Triangle>(owner, isAutoScale, tag, startBarsAgo, endBarsAgo, startTime, endTime,
													startY, endY, color, areaColor, areaOpacity, isGlobal, templateName);
			ChartAnchor	midAnchor	= DrawingTool.CreateChartAnchor(owner, midBarsAgo, midTime, midY);

			midAnchor.CopyDataValues(triangle.MiddleAnchor);
			return triangle;
		}

		// ellipse
		/// <summary>
		/// Draws an ellipse.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static Ellipse Ellipse(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, 
									int endBarsAgo, double endY, Brush brush)
		{
			return ShapeCore<Ellipse>(owner, false, tag, startBarsAgo, endBarsAgo, Core.Globals.MinDate, Core.Globals.MinDate, startY, endY, 
										brush, Brushes.CornflowerBlue, 40, false, null);
		}

		/// <summary>
		/// Draws an ellipse.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		public static Ellipse Ellipse(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, 
										int endBarsAgo, double endY, Brush brush, Brush areaBrush, int areaOpacity)
		{
			return ShapeCore<Ellipse>(owner, false, tag, startBarsAgo, endBarsAgo, Core.Globals.MinDate, Core.Globals.MinDate, 
										startY, endY, brush, areaBrush, areaOpacity, false, null);
		}

		/// <summary>
		/// Draws an ellipse.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static Ellipse Ellipse(NinjaScriptBase owner, string tag, DateTime startTime, double startY, 
										DateTime endTime, double endY, Brush brush)
		{
			return ShapeCore<Ellipse>(owner, false, tag, int.MinValue, int.MinValue, startTime, endTime, startY, endY, brush, Brushes.CornflowerBlue, 40, false, null);
		}

		/// <summary>
		/// Draws an ellipse.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		public static Ellipse Ellipse(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, 
										DateTime endTime, double endY, Brush brush, Brush areaBrush, int areaOpacity)
		{
			return ShapeCore<Ellipse>(owner, false, tag, int.MinValue, int.MinValue, startTime, endTime, startY, endY, brush, areaBrush, areaOpacity, false, null);
		}

		// ellipse -> draw on price panel
		/// <summary>
		/// Draws an ellipse.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static Ellipse Ellipse(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, 
										int endBarsAgo, double endY, Brush brush, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ShapeCore<Ellipse>(owner, false, tag, startBarsAgo, endBarsAgo, Core.Globals.MinDate, Core.Globals.MinDate, 
									startY, endY, brush, Brushes.CornflowerBlue, 40, false, null));
		}

		/// <summary>
		/// Draws an ellipse.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static Ellipse Ellipse(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, 
										int endBarsAgo, double endY, Brush brush, Brush areaBrush, int areaOpacity, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ShapeCore<Ellipse>(owner, false, tag, startBarsAgo, endBarsAgo, Core.Globals.MinDate, Core.Globals.MinDate, 
									startY, endY, brush, areaBrush, areaOpacity, false, null));
		}

		/// <summary>
		/// Draws an ellipse.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static Ellipse Ellipse(NinjaScriptBase owner, string tag, DateTime startTime, double startY, 
									DateTime endTime, double endY, Brush brush, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ShapeCore<Ellipse>(owner, false, tag, int.MinValue, int.MinValue,
					startTime, endTime, startY, endY, brush, Brushes.CornflowerBlue, 40, false, null));
		}

		/// <summary>
		/// Draws an ellipse.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static Ellipse Ellipse(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, 
										DateTime endTime, double endY, Brush brush, Brush areaBrush, int areaOpacity, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ShapeCore<Ellipse>(owner, false, tag, int.MinValue, int.MinValue,
					startTime, endTime, startY, endY, brush, areaBrush, areaOpacity, false, null));
		}

		/// <summary>
		/// Draws an ellipse.
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
		public static Ellipse Ellipse(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, 
									int endBarsAgo, double endY, bool isGlobal, string templateName)
		{
			return ShapeCore<Ellipse>(owner, false, tag, startBarsAgo, endBarsAgo, Core.Globals.MinDate, Core.Globals.MinDate, startY, endY, 
										null, null, -1, isGlobal, templateName);
		}

		/// <summary>
		/// Draws an ellipse.
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
		public static Ellipse Ellipse(NinjaScriptBase owner, string tag, DateTime startTime, double startY, 
									DateTime endTime, double endY, bool isGlobal, string templateName)
		{
			return ShapeCore<Ellipse>(owner, false, tag, int.MinValue, int.MinValue, startTime, endTime, startY, endY, 
										null, null, -1, isGlobal, templateName);
		}

		// rectangle
		/// <summary>
		/// Draws a rectangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static Rectangle Rectangle(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, Brush brush)
		{
			return ShapeCore<Rectangle>(owner, false, tag, startBarsAgo, endBarsAgo, Core.Globals.MinDate, Core.Globals.MinDate, startY, endY,
				brush, Brushes.CornflowerBlue, 40, false, null);
		}

		/// <summary>
		/// Draws a rectangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static Rectangle Rectangle(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY, Brush brush)
		{
			return ShapeCore<Rectangle>(owner, false, tag, int.MinValue, int.MinValue, startTime, endTime, startY, endY,
				brush, Brushes.CornflowerBlue, 40, false, null);
		}

		/// <summary>
		/// Draws a rectangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		public static Rectangle Rectangle(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, 
			double endY, Brush brush, Brush areaBrush, int areaOpacity)
		{
			return ShapeCore<Rectangle>(owner, isAutoScale, tag, startBarsAgo, endBarsAgo, Core.Globals.MinDate, Core.Globals.MinDate, startY, endY,
				brush, areaBrush, areaOpacity, false, null);
		}

		/// <summary>
		/// Draws a rectangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		public static Rectangle Rectangle(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime, 
			double endY, Brush brush, Brush areaBrush, int areaOpacity)
		{
			return ShapeCore<Rectangle>(owner, isAutoScale, tag, int.MinValue, int.MinValue, startTime, endTime, startY, endY,
				brush, areaBrush, areaOpacity, false, null);
		}

		// rectangle -> draw on price panel
		/// <summary>
		/// Draws a rectangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static Rectangle Rectangle(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, 
			Brush brush, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ShapeCore<Rectangle>(owner, false, tag, startBarsAgo, endBarsAgo, 
					Core.Globals.MinDate, Core.Globals.MinDate, startY, endY, brush, Brushes.CornflowerBlue, 40, false, null));
		}

		/// <summary>
		/// Draws a rectangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static Rectangle Rectangle(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, 
			double endY, Brush brush, Brush areaBrush, int areaOpacity, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ShapeCore<Rectangle>(owner, isAutoScale, tag, startBarsAgo, endBarsAgo,
					Core.Globals.MinDate, Core.Globals.MinDate, startY, endY, brush, areaBrush, areaOpacity, false, null));
		}

		/// <summary>
		/// Draws a rectangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static Rectangle Rectangle(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime, 
			double endY, Brush brush, Brush areaBrush, int areaOpacity, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ShapeCore<Rectangle>(owner, isAutoScale, tag, int.MinValue, int.MinValue,
					startTime, endTime, startY, endY, brush, areaBrush, areaOpacity, false, null));
		}

		/// <summary>
		/// Draws a rectangle.
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
		public static Rectangle Rectangle(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, bool isGlobal, string templateName)
		{
			return ShapeCore<Rectangle>(owner, false, tag, startBarsAgo, endBarsAgo, Core.Globals.MinDate, Core.Globals.MinDate, startY, endY,
				null, null, -1, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a rectangle.
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
		public static Rectangle Rectangle(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY, bool isGlobal, string templateName)
		{
			return ShapeCore<Rectangle>(owner, false, tag, int.MinValue, int.MinValue, startTime, endTime, startY, endY,
				null, null, -1, isGlobal, templateName);
		}

		// triangle
		/// <summary>
		/// Draws a triangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="middleBarsAgo">The number of bars ago (x value) of the 2nd anchor point</param>
		/// <param name="middleY">The y value of the 2nd anchor point</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static Triangle Triangle(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int middleBarsAgo, double middleY, 
			int endBarsAgo, double endY, Brush brush)
		{
			return TriangleCore(owner, false, tag, startBarsAgo, middleBarsAgo, endBarsAgo, Core.Globals.MinDate, Core.Globals.MinDate,
				Core.Globals.MinDate, startY, middleY, endY, brush, Brushes.CornflowerBlue, 40, false, null);
		}

		/// <summary>
		/// Draws a triangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="middleTime">The middle time.</param>
		/// <param name="middleY">The y value of the 2nd anchor point</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static Triangle Triangle(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime middleTime, double middleY, 
			DateTime endTime, double endY, Brush brush)
		{
			return TriangleCore(owner, false, tag, int.MinValue, int.MinValue, int.MinValue, startTime, middleTime,
				endTime, startY, middleY, endY, brush, Brushes.CornflowerBlue, 40, false, null);
		}

		/// <summary>
		/// Draws a triangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="middleBarsAgo">The number of bars ago (x value) of the 2nd anchor point</param>
		/// <param name="middleY">The y value of the 2nd anchor point</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		public static Triangle Triangle(NinjaScriptBase owner,string tag, bool isAutoScale, int startBarsAgo, double startY, int middleBarsAgo, double middleY, 
			int endBarsAgo, double endY, Brush brush, Brush areaBrush, int areaOpacity)
		{
			return TriangleCore(owner, isAutoScale, tag, startBarsAgo, middleBarsAgo, endBarsAgo, Core.Globals.MinDate, Core.Globals.MinDate,
				Core.Globals.MinDate, startY, middleY, endY, brush, areaBrush, areaOpacity, false, null);
		}

		/// <summary>
		/// Draws a triangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="midTime">The time of the 2nd anchor point</param>
		/// <param name="middleY">The y value of the 2nd anchor point</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <returns></returns>
		public static Triangle Triangle(NinjaScriptBase owner,string tag, bool isAutoScale, DateTime startTime, double startY,
			DateTime midTime, double middleY, DateTime endTime, double endY, Brush brush, Brush areaBrush, int areaOpacity)
		{
			return TriangleCore(owner, isAutoScale, tag, int.MinValue, int.MinValue, int.MinValue, 
				startTime, midTime, endTime, startY, middleY, endY, brush, areaBrush, areaOpacity, false, null);
		}

		// triangle -> draw on price panel
		/// <summary>
		/// Draws a triangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="middleBarsAgo">The number of bars ago (x value) of the 2nd anchor point</param>
		/// <param name="middleY">The y value of the 2nd anchor point</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static Triangle Triangle(NinjaScriptBase owner,string tag, int startBarsAgo, double startY, int middleBarsAgo, double middleY, 
			int endBarsAgo, double endY, Brush brush, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				TriangleCore(owner, false, tag, startBarsAgo, middleBarsAgo, endBarsAgo, Core.Globals.MinDate, 
					Core.Globals.MinDate,Core.Globals.MinDate, startY, middleY, endY, brush, Brushes.CornflowerBlue, 40, false, null));
		}

		/// <summary>
		/// Draws a triangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="middleBarsAgo">The number of bars ago (x value) of the 2nd anchor point</param>
		/// <param name="middleY">The y value of the 2nd anchor point</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static Triangle Triangle(NinjaScriptBase owner,string tag, bool isAutoScale, int startBarsAgo, double startY, int middleBarsAgo, double middleY, 
			int endBarsAgo, double endY, Brush brush, Brush areaBrush, int areaOpacity, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				TriangleCore(owner, isAutoScale, tag, startBarsAgo, middleBarsAgo, endBarsAgo, Core.Globals.MinDate, Core.Globals.MinDate,
					Core.Globals.MinDate, startY, middleY, endY, brush, areaBrush, areaOpacity, false, null));
		}

		/// <summary>
		/// Draws a triangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="midTime">The time of the 2nd anchor point</param>
		/// <param name="middleY">The y value of the 2nd anchor point</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="areaBrush">The brush used to color the fill region area of the draw object</param>
		/// <param name="areaOpacity"> Sets the level of transparency for the fill color. Valid values between 0 - 100. (0 = completely transparent, 100 = no opacity)</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static Triangle Triangle(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY,
			DateTime midTime, double middleY, DateTime endTime, double endY, Brush brush, Brush areaBrush,
			int areaOpacity, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				TriangleCore(owner, isAutoScale, tag, int.MinValue, int.MinValue, int.MinValue, 
					startTime, midTime, endTime, startY, middleY, endY, brush, areaBrush, areaOpacity, false, null));
		}

		/// <summary>
		/// Draws a triangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="middleBarsAgo">The number of bars ago (x value) of the 2nd anchor point</param>
		/// <param name="middleY">The y value of the 2nd anchor point</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static Triangle Triangle(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int middleBarsAgo, double middleY, 
			int endBarsAgo, double endY, bool isGlobal, string templateName)
		{
			return TriangleCore(owner, false, tag, startBarsAgo, middleBarsAgo, endBarsAgo, Core.Globals.MinDate, Core.Globals.MinDate,
				Core.Globals.MinDate, startY, middleY, endY, null, null, -1, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a triangle.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="middleTime">The middle time.</param>
		/// <param name="middleY">The y value of the 2nd anchor point</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static Triangle Triangle(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime middleTime, double middleY, 
			DateTime endTime, double endY, bool isGlobal, string templateName)
		{
			return TriangleCore(owner, false, tag, int.MinValue, int.MinValue, int.MinValue, startTime, middleTime,
				endTime, startY, middleY, endY, null, null, -1, isGlobal, templateName);
		}

	}
}
