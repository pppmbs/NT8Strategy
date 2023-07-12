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
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;

#endregion

namespace NinjaTrader.NinjaScript.DrawingTools
{
	/// <summary>
	/// Represents an interface that exposes information regarding an Arrow Line IDrawingTool.
	/// </summary>
	public class ArrowLine : Line
	{
		public override object Icon { get { return Gui.Tools.Icons.DrawArrowLine; } }
		protected override void OnStateChange()
		{
			base.OnStateChange();
			if (State == State.SetDefaults)
			{
				LineType									= ChartLineType.ArrowLine;
				Name										= Custom.Resource.NinjaScriptDrawingToolArrowLine;
			}
		}
	}

	/// <summary>
	/// Represents an interface that exposes information regarding an Extended Line IDrawingTool.
	/// </summary>
	public class ExtendedLine : Line
	{
		public override object Icon { get { return Gui.Tools.Icons.DrawExtendedLineTo; } }
		protected override void OnStateChange()
		{
			base.OnStateChange();
			if (State == State.SetDefaults)
			{
				LineType	= ChartLineType.ExtendedLine;
				Name		= Custom.Resource.NinjaScriptDrawingToolExtendedLine;
			}
		}
	}

	/// <summary>
	/// Represents an interface that exposes information regarding a Horizontal Line IDrawingTool.
	/// </summary>
	public class HorizontalLine : Line
	{
		// override this, we only need operations on a single anchor
		public override IEnumerable<ChartAnchor> Anchors { get { return new[] { StartAnchor }; } }

		public override object Icon { get { return Gui.Tools.Icons.DrawHorizLineTool; } }

		protected override void OnStateChange()
		{
			base.OnStateChange();
			if (State == State.SetDefaults)
			{
				EndAnchor.IsBrowsable				= false;
				LineType							= ChartLineType.HorizontalLine;
				Name								= Custom.Resource.NinjaScriptDrawingToolHorizontalLine;
				StartAnchor.DisplayName				= Custom.Resource.NinjaScriptDrawingToolAnchor;
				StartAnchor.IsXPropertiesVisible	= false;
			}
		}
	}

	/// <summary>
	/// Represents an interface that exposes information regarding a Line IDrawingTool.
	/// </summary>
	public class Line : DrawingTool
	{
		// this line class takes care of all stock line types, so we use this to keep track
		// of what kind of line instances this is. Localization is not needed because it's not visible on ui
		protected enum ChartLineType
		{
			ArrowLine,
			ExtendedLine,
			HorizontalLine,
			Line,
			Ray,
			VerticalLine,
		}

		public override IEnumerable<ChartAnchor> Anchors { get { return new[] { StartAnchor, EndAnchor }; } }
		[Display(Order = 2)]
		public ChartAnchor	EndAnchor		{ get; set; }
		[Display(Order = 1)]
		public ChartAnchor StartAnchor		{ get; set; }

		public override object Icon			{ get { return Gui.Tools.Icons.DrawLineTool; } }

		[CLSCompliant(false)]
		protected		SharpDX.Direct2D1.PathGeometry		ArrowPathGeometry;
		private	const	double								cursorSensitivity		= 15;
		private			ChartAnchor							editingAnchor;

		[Browsable(false)]
		[XmlIgnore]
		protected ChartLineType LineType { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), GroupName = "NinjaScriptGeneral", Name = "NinjaScriptDrawingToolLine", Order = 99)]
		public Stroke Stroke { get; set; }

		public override bool SupportsAlerts { get { return true; } }

		private ChartAnchor Anchor45(ChartAnchor starAnchort, ChartAnchor endAnchor, ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale)
		{
			if (!Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
				return endAnchor;

			Point	startPoint	= starAnchort.GetPoint(chartControl, chartPanel, chartScale);
			Point	endPoint	= endAnchor.GetPoint(chartControl, chartPanel, chartScale);

			double	diffX		= endPoint.X - startPoint.X;
			double	diffY		= endPoint.Y - startPoint.Y;

			double	length		= Math.Sqrt(diffX * diffX + diffY * diffY);

			double	angle		= Math.Atan2(diffY, diffX);

			double step			= Math.PI / 8;
			double targetAngle	= 0;

			if (angle > Math.PI - step || angle < -Math.PI + step)	targetAngle = Math.PI;
			else if (angle > Math.PI - step * 3)					targetAngle = Math.PI - step * 2;
			else if (angle > Math.PI - step * 5)					targetAngle = Math.PI - step * 4;
			else if (angle > Math.PI - step * 7)					targetAngle = Math.PI - step * 6;
			else if (angle < -Math.PI + step * 3)					targetAngle = -Math.PI + step * 2;
			else if (angle < -Math.PI + step * 5)					targetAngle = -Math.PI + step * 4;
			else if (angle < -Math.PI + step * 7)					targetAngle = -Math.PI + step * 6;

			Point		targetPoint = new Point(startPoint.X + Math.Cos(targetAngle) * length, startPoint.Y + Math.Sin(targetAngle) * length);
			ChartAnchor	ret			= new ChartAnchor();

			ret.UpdateFromPoint(targetPoint, chartControl, chartScale);

			if (startPoint.X == targetPoint.X)
			{
				ret.Time		= starAnchort.Time;
				ret.SlotIndex	=starAnchort.SlotIndex;
			}
			else if (startPoint.Y == targetPoint.Y)
				ret.Price = starAnchort.Price;

			return ret;
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
					if (LineType == ChartLineType.HorizontalLine || LineType == ChartLineType.VerticalLine)
						return Cursors.SizeAll;
					return editingAnchor == StartAnchor ? Cursors.SizeNESW : Cursors.SizeNWSE;
				default:
					// draw move cursor if cursor is near line path anywhere
					Point startPoint = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);

					if (LineType == ChartLineType.HorizontalLine || LineType == ChartLineType.VerticalLine)
					{
						// just go by single axis since we know the entire lines position
						if (LineType == ChartLineType.VerticalLine && Math.Abs(point.X - startPoint.X) <= cursorSensitivity)
							return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
						if (LineType == ChartLineType.HorizontalLine && Math.Abs(point.Y - startPoint.Y) <= cursorSensitivity)
							return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
						return null;
					}

					ChartAnchor closest = GetClosestAnchor(chartControl, chartPanel, chartScale, cursorSensitivity, point);
					if (closest != null)
					{
						if (IsLocked)
							return Cursors.Arrow;
						return closest == StartAnchor ? Cursors.SizeNESW : Cursors.SizeNWSE;
					}

					Point	endPoint		= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
					Point	minPoint		= startPoint;
					Point	maxPoint		= endPoint;

					// if we're an extended or ray line, we want to use min & max points in vector used for hit testing
					if (LineType == ChartLineType.ExtendedLine)
					{
						// adjust vector to include min all the way to max points
						minPoint	= GetExtendedPoint(chartControl, chartPanel, chartScale, EndAnchor, StartAnchor);
						maxPoint	= GetExtendedPoint(chartControl, chartPanel, chartScale, StartAnchor, EndAnchor);
					}
					else if (LineType == ChartLineType.Ray)
						maxPoint	= GetExtendedPoint(chartControl, chartPanel, chartScale, StartAnchor, EndAnchor);

					Vector	totalVector	= maxPoint - minPoint;
					return MathHelper.IsPointAlongVector(point, minPoint, totalVector, cursorSensitivity) ?
						IsLocked ? Cursors.Arrow : Cursors.SizeAll : null;
			}
		}

		public override IEnumerable<AlertConditionItem> GetAlertConditionItems()
		{
			yield return new AlertConditionItem
			{
				Name					= Custom.Resource.NinjaScriptDrawingToolLine,
				ShouldOnlyDisplayName	= true
			};
		}

		public sealed override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel	chartPanel	= chartControl.ChartPanels[chartScale.PanelIndex];
			Point		startPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point		endPoint	= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);

			int			totalWidth	= chartPanel.W + chartPanel.X;
			int			totalHeight	= chartPanel.Y + chartPanel.H;

			if (LineType == ChartLineType.VerticalLine)
				return new[] { new Point(startPoint.X, chartPanel.Y), new Point(startPoint.X, chartPanel.Y + ((totalHeight - chartPanel.Y) / 2d)), new Point(startPoint.X, totalHeight) };
			if (LineType == ChartLineType.HorizontalLine)
				return new[] { new Point(chartPanel.X, startPoint.Y), new Point(totalWidth / 2d, startPoint.Y), new Point(totalWidth, startPoint.Y) };

			//Vector strokeAdj = new Vector(Stroke.Width / 2, Stroke.Width / 2);
			Point midPoint = startPoint + ((endPoint - startPoint) / 2);
			return new[]{ startPoint, midPoint, endPoint };
		}

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values, ChartControl chartControl, ChartScale chartScale)
		{
			if (values.Length < 1)
				return false;
			ChartPanel chartPanel = chartControl.ChartPanels[PanelIndex];
			// h line and v line have much more simple alert handling
			if (LineType == ChartLineType.HorizontalLine)
			{
				double barVal	= values[0].Value;
				double lineVal	= conditionItem.Offset.Calculate(StartAnchor.Price, AttachedTo.Instrument);

				switch (condition)
				{
					case Condition.Equals:			return barVal.ApproxCompare(lineVal) == 0;
					case Condition.NotEqual:		return barVal.ApproxCompare(lineVal) != 0;
					case Condition.Greater:			return barVal > lineVal;
					case Condition.GreaterEqual:	return barVal >= lineVal;
					case Condition.Less:			return barVal < lineVal;
					case Condition.LessEqual:		return barVal <= lineVal;
					case Condition.CrossAbove:
					case Condition.CrossBelow:
						Predicate<ChartAlertValue> predicate = v =>
						{
							if (condition == Condition.CrossAbove)
								return v.Value > lineVal;
							return v.Value < lineVal;
						};
						return MathHelper.DidPredicateCross(values, predicate);
				}
				return false;
			}

			// get start / end points of what is absolutely shown for our vector
			Point lineStartPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point lineEndPoint		= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);

			if (LineType == ChartLineType.ExtendedLine || LineType == ChartLineType.Ray)
			{
				// need to adjust vector to rendered extensions
				Point maxPoint = GetExtendedPoint(chartControl, chartPanel, chartScale, StartAnchor, EndAnchor);
				if (LineType == ChartLineType.ExtendedLine)
				{
					Point minPoint = GetExtendedPoint(chartControl, chartPanel, chartScale,EndAnchor, StartAnchor);
					lineStartPoint = minPoint;
				}
				lineEndPoint = maxPoint;
			}

			double minLineX = double.MaxValue;
			double maxLineX = double.MinValue;

			foreach (Point point in new[]{lineStartPoint, lineEndPoint})
			{
				minLineX = Math.Min(minLineX, point.X);
				maxLineX = Math.Max(maxLineX, point.X);
			}

			// first thing, if our smallest x is greater than most recent bar, we have nothing to do yet.
			// do not try to check Y because lines could cross through stuff
			double firstBarX = values[0].ValueType == ChartAlertValueType.StaticValue ? minLineX : chartControl.GetXByTime(values[0].Time);
			double firstBarY = chartScale.GetYByValue(values[0].Value);

			// dont have to take extension into account as its already handled in min/max line x

			// bars completely passed our line
			if (maxLineX < firstBarX)
				return false;

			// bars not yet to our line
			if (minLineX > firstBarX)
				return false;

			// NOTE: normalize line points so the leftmost is passed first. Otherwise, our vector
			// math could end up having the line normal vector being backwards if user drew it backwards.
			// but we dont care the order of anchors, we want 'up' to mean 'up'!
			Point leftPoint		= lineStartPoint.X < lineEndPoint.X ? lineStartPoint : lineEndPoint;
			Point rightPoint	= lineEndPoint.X > lineStartPoint.X ? lineEndPoint : lineStartPoint;

			Point barPoint = new Point(firstBarX, firstBarY);
			// NOTE: 'left / right' is relative to if line was vertical. it can end up backwards too
			MathHelper.PointLineLocation pointLocation = MathHelper.GetPointLineLocation(leftPoint, rightPoint, barPoint);
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
						double barX = chartControl.GetXByTime(v.Time);
						double barY = chartScale.GetYByValue(v.Value);
						Point stepBarPoint = new Point(barX, barY);
						MathHelper.PointLineLocation ptLocation = MathHelper.GetPointLineLocation(leftPoint, rightPoint, stepBarPoint);
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
			if (DrawingState == DrawingState.Building)
				return true;

			DateTime	minTime = Core.Globals.MaxDate;
			DateTime	maxTime = Core.Globals.MinDate;

			if (LineType != ChartLineType.ExtendedLine && LineType != ChartLineType.Ray)
			{
				// make sure our 1 anchor is in time frame
				if (LineType == ChartLineType.VerticalLine)
					return StartAnchor.Time >= firstTimeOnChart && StartAnchor.Time <= lastTimeOnChart;

				// check at least one of our anchors is in horizontal time frame
				foreach (ChartAnchor anchor in Anchors)
				{
					if (anchor.Time < minTime)
						minTime = anchor.Time;
					if (anchor.Time > maxTime)
						maxTime = anchor.Time;
				}
			}
			else
			{
				// extended line, rays: here we'll get extended point and see if they're on scale
				ChartPanel	panel		= chartControl.ChartPanels[PanelIndex];
				Point		startPoint	= StartAnchor.GetPoint(chartControl, panel, chartScale);

				Point		minPoint	= startPoint;
				Point		maxPoint	= GetExtendedPoint(chartControl, panel, chartScale, StartAnchor, EndAnchor);

				if (LineType == ChartLineType.ExtendedLine)
					minPoint = GetExtendedPoint(chartControl, panel, chartScale, EndAnchor, StartAnchor);

				foreach (Point pt in new[] { minPoint, maxPoint })
				{
					DateTime time = chartControl.GetTimeByX((int) pt.X);
					if (time > maxTime)
						maxTime = time;
					if (time < minTime)
						minTime = time;
				}
			}

			// check offscreen vertically. make sure to check the line doesnt cut through the scale, so check both are out
			if (LineType == ChartLineType.HorizontalLine && (StartAnchor.Price < chartScale.MinValue || StartAnchor.Price > chartScale.MaxValue) && !IsAutoScale)
				return false; // horizontal line only has one anchor to whiff

			// hline extends, but otherwise try to check if line horizontally crosses through visible chart times in some way
			if (LineType != ChartLineType.HorizontalLine && (minTime > lastTimeOnChart || maxTime < firstTimeOnChart))
				return false;

			return true;
		}

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;

			if (!IsVisible)
				return;

			// make sure to set good min/max values on single click lines as well, in case anchor left in editing
			if (LineType == ChartLineType.HorizontalLine)
				MinValue = MaxValue = Anchors.First().Price;
			else if (LineType != ChartLineType.VerticalLine)
			{
				// return min/max values only if something has been actually drawn
				if (Anchors.Any(a => !a.IsEditing))
					foreach (ChartAnchor anchor in Anchors)
					{
						MinValue = Math.Min(anchor.Price, MinValue);
						MaxValue = Math.Max(anchor.Price, MaxValue);
					}
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
						StartAnchor.IsEditing = false;

						// these lines only need one anchor, so stop editing end anchor too
						if (LineType == ChartLineType.HorizontalLine || LineType == ChartLineType.VerticalLine)
							EndAnchor.IsEditing = false;

						// give end anchor something to start with so we dont try to render it with bad values right away
						dataPoint.CopyDataValues(EndAnchor);
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
					// see if they clicked near a point to edit, if so start editing
					if (LineType == ChartLineType.HorizontalLine || LineType == ChartLineType.VerticalLine)
					{
						if (GetCursor(chartControl, chartPanel, chartScale, point) == null)
							IsSelected = false;
						else
						{
							// we dont care here, since we're moving just one anchor
							editingAnchor = StartAnchor;
						}
					}
					else
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
						// user whiffed.
							IsSelected = false;
					}
					break;
			}
		}

		public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (IsLocked && DrawingState != DrawingState.Building)
				return;

			IgnoresSnapping = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

			if (DrawingState == DrawingState.Building)
			{
				// start anchor will not be editing here because we start building as soon as user clicks, which
				// plops down a start anchor right away
				if (EndAnchor.IsEditing)
					Anchor45(StartAnchor, dataPoint, chartControl, chartPanel, chartScale).CopyDataValues(EndAnchor);
			}
			else if (DrawingState == DrawingState.Editing && editingAnchor != null)
			{
				// if its a line with two anchors, update both x/y at once
				if (LineType != ChartLineType.HorizontalLine && LineType != ChartLineType.VerticalLine)
				{
					ChartAnchor startAnchor = editingAnchor == StartAnchor ? EndAnchor : StartAnchor;
					Anchor45(startAnchor, dataPoint, chartControl, chartPanel, chartScale).CopyDataValues(editingAnchor);
				}
				else if (LineType != ChartLineType.VerticalLine)
				{
					// horizontal line only needs Y value updated
					editingAnchor.Price = dataPoint.Price;
					EndAnchor.Price		= dataPoint.Price;
				}
				else
				{
					// vertical line only needs X value updated
					editingAnchor.Time		= dataPoint.Time;
					editingAnchor.SlotIndex	= dataPoint.SlotIndex;
				}
			}
			else if (DrawingState == DrawingState.Moving)
				foreach (ChartAnchor anchor in Anchors)
					// only move anchor values as needed depending on line type
					if (LineType == ChartLineType.HorizontalLine)
						anchor.MoveAnchorPrice(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
					else if (LineType == ChartLineType.VerticalLine)
						anchor.MoveAnchorTime(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
					else
						anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
			//lastMouseMovePoint.Value, point, chartControl, chartScale);
		}

		public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			// simply end whatever moving
			if (DrawingState == DrawingState.Moving || DrawingState == DrawingState.Editing)
				DrawingState = DrawingState.Normal;
			if (editingAnchor != null)
				editingAnchor.IsEditing = false;
			editingAnchor = null;
		}

		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (Stroke == null)
				return;

			Stroke.RenderTarget									= RenderTarget;

			SharpDX.Direct2D1.AntialiasMode	oldAntiAliasMode	= RenderTarget.AntialiasMode;
			RenderTarget.AntialiasMode							= SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			ChartPanel						panel				= chartControl.ChartPanels[chartScale.PanelIndex];
			Point							startPoint			= StartAnchor.GetPoint(chartControl, panel, chartScale);

			// align to full pixel to avoid unneeded aliasing
			double							strokePixAdj		= ((double)(Stroke.Width % 2)).ApproxCompare(0) == 0 ? 0.5d : 0d;
			Vector							pixelAdjustVec		= new Vector(strokePixAdj, strokePixAdj);

			if (LineType == ChartLineType.HorizontalLine || LineType == ChartLineType.VerticalLine)
			{
				// horizontal and vertical line only need single anchor (StartAnchor) to draw
				// so just go by panel bounds. Keep in mind the panel may not start at 0
				Point startAdj	= (LineType == ChartLineType.HorizontalLine ? new Point(panel.X, startPoint.Y) : new Point(startPoint.X, panel.Y)) + pixelAdjustVec;
				Point endAdj	= (LineType == ChartLineType.HorizontalLine ? new Point(panel.X + panel.W, startPoint.Y) : new Point(startPoint.X, panel.Y + panel.H)) + pixelAdjustVec;
				RenderTarget.DrawLine(startAdj.ToVector2(), endAdj.ToVector2(), Stroke.BrushDX, Stroke.Width, Stroke.StrokeStyle);
				return;
			}

			Point					endPoint			= EndAnchor.GetPoint(chartControl, panel, chartScale);

			// convert our start / end pixel points to directx 2d vectors
			Point					endPointAdjusted	= endPoint + pixelAdjustVec;
			SharpDX.Vector2			endVec				= endPointAdjusted.ToVector2();
			Point					startPointAdjusted	= startPoint + pixelAdjustVec;
			SharpDX.Vector2			startVec			= startPointAdjusted.ToVector2();
			SharpDX.Direct2D1.Brush	tmpBrush			= IsInHitTest ? chartControl.SelectionBrush : Stroke.BrushDX;

			// if a plain ol' line, then we're all done
			// if we're an arrow line, make sure to draw the actual line. for extended lines, only a single
			// line to extended points is drawn below, to avoid unneeded multiple DrawLine calls
			if (LineType == ChartLineType.Line)
			{
				RenderTarget.DrawLine(startVec, endVec, tmpBrush, Stroke.Width, Stroke.StrokeStyle);
				return;
			}
			// we have a line type with extensions (ray / extended line) or additional drawing needed
			// create a line vector to easily calculate total length
			Vector lineVector = endPoint - startPoint;
			lineVector.Normalize();

			if (LineType != ChartLineType.ArrowLine)
			{
				Point minPoint = startPointAdjusted;
				Point maxPoint = GetExtendedPoint(chartControl, panel, chartScale, StartAnchor, EndAnchor);//GetExtendedPoint(startPoint, endPoint); //
				if (LineType == ChartLineType.ExtendedLine)
					minPoint = GetExtendedPoint(chartControl, panel, chartScale, EndAnchor, StartAnchor);
				RenderTarget.DrawLine(minPoint.ToVector2(), maxPoint.ToVector2(), tmpBrush, Stroke.Width, Stroke.StrokeStyle);
			}
			else
			{
				// translate to the angle the line is pointing to simplify drawing the arrow rect
				// the ArrowPathGeometry is created with 0,0 as arrow point, so transform there as well
				// note rotation is against zero, not end vector
				RenderTarget.DrawLine(startVec, endVec, tmpBrush, Stroke.Width, Stroke.StrokeStyle);
				float				vectorAngle			= -(float)Math.Atan2(lineVector.X, lineVector.Y);

				// adjust end vector slightly to cover edges of line stroke
				Vector				adjustVector		= lineVector * 5;
				SharpDX.Vector2		arrowPointVec		= new SharpDX.Vector2((float)(endVec.X + adjustVector.X), (float)(endVec.Y + adjustVector.Y));
				// rotate and scale our arrow to stroke size, the geo is created as a fixed width of 10
				// make sure to rotate, then scale before translating so we end up in the right place
				SharpDX.Matrix3x2	transformMatrix2	= SharpDX.Matrix3x2.Rotation(vectorAngle, SharpDX.Vector2.Zero)
					* SharpDX.Matrix3x2.Scaling((float)Math.Max(1.0f, Stroke.Width *.45) + 0.25f) * SharpDX.Matrix3x2.Translation(arrowPointVec);
				if (ArrowPathGeometry == null)
				{

					// create our arrow directx geometry.
					// just make a static size we will scale when drawing
					// all relative to top of line
					// nudge up y slightly to cover up top of stroke (instead of using zero),
					// half the stroke will hide any overlap
					ArrowPathGeometry								= new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
					SharpDX.Direct2D1.GeometrySink	geometrySink	= ArrowPathGeometry.Open();
					SharpDX.Vector2					top				= new SharpDX.Vector2(0, Stroke.Width * 0.5f);
					float							arrowWidth		= 6f;

					geometrySink.BeginFigure(top, SharpDX.Direct2D1.FigureBegin.Filled);
					geometrySink.AddLine(new SharpDX.Vector2(arrowWidth, -arrowWidth));
					geometrySink.AddLine(new SharpDX.Vector2(-arrowWidth, -arrowWidth));
					geometrySink.AddLine(top);// cap off figure
					geometrySink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
					geometrySink.Close();
				}

				RenderTarget.Transform = transformMatrix2;

				RenderTarget.FillGeometry(ArrowPathGeometry, tmpBrush);
				RenderTarget.Transform = SharpDX.Matrix3x2.Identity;
			}
			RenderTarget.AntialiasMode	= oldAntiAliasMode;
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				LineType					= ChartLineType.Line;
				Name						= Custom.Resource.NinjaScriptDrawingToolLine;
				DrawingState				= DrawingState.Building;

				EndAnchor					= new ChartAnchor
				{
					IsEditing		= true,
					DrawingTool		= this,
					DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorEnd,
					IsBrowsable		= true
				};

				StartAnchor			= new ChartAnchor
				{
					IsEditing		= true,
					DrawingTool		= this,
					DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorStart,
					IsBrowsable		= true
				};

				// a normal line with both end points has two anchors
				Stroke						= new Stroke(Brushes.CornflowerBlue, 2f);
			}
			else if (State == State.Terminated)
			{
				// release any device resources
				Dispose();
			}
		}
	}

	/// <summary>
	/// Represents an interface that exposes information regarding a Vertical Line IDrawingTool.
	/// </summary>
	public class VerticalLine : Line
	{
		// override this, we only need operations on a single anchor
		public override IEnumerable<ChartAnchor> Anchors { get { return new[] { StartAnchor }; } }

		public override object Icon { get { return Gui.Tools.Icons.DrawVertLineTool; } }

		protected override void OnStateChange()
		{
			base.OnStateChange();
			if (State == State.SetDefaults)
			{
				EndAnchor.IsBrowsable				= false;
				LineType							= ChartLineType.VerticalLine;
				Name								= Custom.Resource.NinjaScriptDrawingToolVerticalLine;
				StartAnchor.DisplayName				= Custom.Resource.NinjaScriptDrawingToolAnchor;
				StartAnchor.IsYPropertyVisible		= false;
			}
		}

		public override bool SupportsAlerts	{ get { return false; } }
	}

	/// <summary>
	/// Represents an interface that exposes information regarding a Ray IDrawingTool.
	/// </summary>
	public class Ray : Line
	{
		public override object Icon { get { return Gui.Tools.Icons.DrawRay; } }

		protected override void OnStateChange()
		{
			base.OnStateChange();
			if (State == State.SetDefaults)
			{
				LineType	= ChartLineType.Ray;
				Name		= Custom.Resource.NinjaScriptDrawingToolRay;
			}
		}
	}

	public static partial class Draw
	{
		private static T DrawLineTypeCore<T>(NinjaScriptBase owner, bool isAutoScale, string tag,
										int startBarsAgo, DateTime startTime, double startY, int endBarsAgo, DateTime endTime, double endY,
										Brush brush, DashStyleHelper dashStyle, int width, bool isGlobal, string templateName) where T : Line
		{
			if (owner == null)
				throw new ArgumentException("owner");

			if (string.IsNullOrWhiteSpace(tag))
				throw new ArgumentException(@"tag cant be null or empty", "tag");

			if (isGlobal && tag[0] != GlobalDrawingToolManager.GlobalDrawingToolTagPrefix)
				tag = string.Format("{0}{1}", GlobalDrawingToolManager.GlobalDrawingToolTagPrefix, tag);

			T lineT = DrawingTool.GetByTagOrNew(owner, typeof(T), tag, templateName) as T;

			if (lineT == null)
				return null;

			if (lineT is VerticalLine)
			{
				if (startTime == Core.Globals.MinDate && startBarsAgo == int.MinValue)
					throw new ArgumentException("missing vertical line time / bars ago");
			}
			else if (lineT is HorizontalLine)
			{
				if (startY.ApproxCompare(double.MinValue) == 0)
					throw new ArgumentException("missing horizontal line Y");
			}
			else if (startTime == Core.Globals.MinDate && endTime == Core.Globals.MinDate && startBarsAgo == int.MinValue && endBarsAgo == int.MinValue)
				throw new ArgumentException("bad start/end date/time");

			DrawingTool.SetDrawingToolCommonValues(lineT, tag, isAutoScale, owner, isGlobal);

			// dont nuke existing anchor refs on the instance
			ChartAnchor startAnchor;

			// check if its one of the single anchor lines
			if (lineT is HorizontalLine || lineT is VerticalLine)
			{
				startAnchor = DrawingTool.CreateChartAnchor(owner, startBarsAgo, startTime, startY);
				startAnchor.CopyDataValues(lineT.StartAnchor);
			}
			else
			{
				startAnchor				= DrawingTool.CreateChartAnchor(owner, startBarsAgo, startTime, startY);
				ChartAnchor endAnchor	= DrawingTool.CreateChartAnchor(owner, endBarsAgo, endTime, endY);
				startAnchor.CopyDataValues(lineT.StartAnchor);
				endAnchor.CopyDataValues(lineT.EndAnchor);
			}

			if (brush != null)
				lineT.Stroke = new Stroke(brush, dashStyle, width) { RenderTarget = lineT.Stroke.RenderTarget };

			lineT.SetState(State.Active);
			return lineT;
		}

		// arrow line overloads
		private static ArrowLine ArrowLineCore(NinjaScriptBase owner, bool isAutoScale, string tag,
											int startBarsAgo, DateTime startTime, double startY, int endBarsAgo, DateTime endTime, double endY,
											Brush brush, DashStyleHelper dashStyle, int width, bool isGlobal, string templateName)
		{
			return DrawLineTypeCore<ArrowLine>(owner, isAutoScale,tag, startBarsAgo, startTime, startY, endBarsAgo, endTime, endY,
				brush, dashStyle, width, isGlobal, templateName);
		}

		/// <summary>
		/// Draws an arrow line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static ArrowLine ArrowLine(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, Brush brush)
		{
			return ArrowLineCore(owner, false, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, brush,
				DashStyleHelper.Solid, 1, false, null);
		}

		/// <summary>
		/// Draws an arrow line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static ArrowLine ArrowLine(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY, Brush brush)
		{
			return ArrowLineCore(owner, false, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, brush,
				DashStyleHelper.Solid, 1, false, null);
		}

		/// <summary>
		/// Draws an arrow line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <returns></returns>
		public static ArrowLine ArrowLine(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY,
			Brush brush, DashStyleHelper dashStyle, int width)
		{
			return ArrowLineCore(owner, false, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, brush, dashStyle, width, false, null);
		}

		/// <summary>
		/// Draws an arrow line.
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
		public static ArrowLine ArrowLine(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, double endY,
			Brush brush, DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ArrowLineCore(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, brush, dashStyle, width, false, null));
		}

		/// <summary>
		/// Draws an arrow line.
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
		public static ArrowLine ArrowLine(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime, double endY,
			Brush brush, DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ArrowLineCore(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, brush, dashStyle, width, false, null));
		}

		/// <summary>
		/// Draws an arrow line.
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
		public static ArrowLine ArrowLine(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, bool isGlobal, string templateName)
		{
			return ArrowLineCore(owner, false, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, null,
				DashStyleHelper.Solid, 1, isGlobal, templateName);
		}

		/// <summary>
		/// Draws an arrow line.
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
		public static ArrowLine ArrowLine(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY,  bool isGlobal, string templateName)
		{
			return ArrowLineCore(owner, false, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, null,
				DashStyleHelper.Solid, 1, isGlobal, templateName);
		}

		// extended line overloads
		private static ExtendedLine ExtendedLineCore(NinjaScriptBase owner, bool isAutoScale, string tag,
												int startBarsAgo, DateTime startTime, double startY, int endBarsAgo, DateTime endTime, double endY,
												Brush brush, DashStyleHelper dashStyle, int width, bool isGlobal, string templateName)
		{
			return DrawLineTypeCore<ExtendedLine>(owner, isAutoScale,tag, startBarsAgo, startTime, startY, endBarsAgo, endTime, endY,
				brush, dashStyle, width, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a line with infinite end points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static ExtendedLine ExtendedLine(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, Brush brush)
		{
			return ExtendedLineCore(owner, false, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, brush,
				DashStyleHelper.Solid, 1, false, null);
		}

		/// <summary>
		/// Draws a line with infinite end points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static ExtendedLine ExtendedLine(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY, Brush brush)
		{
			return ExtendedLineCore(owner, false, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, brush,
				DashStyleHelper.Solid, 1, false, null);
		}

		/// <summary>
		/// Draws a line with infinite end points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <returns></returns>
		public static ExtendedLine ExtendedLine(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY,
			Brush brush, DashStyleHelper dashStyle, int width)
		{
			return ExtendedLineCore(owner, false, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY,
								brush, dashStyle, width, false, null);
		}

		/// <summary>
		/// Draws a line with infinite end points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <returns></returns>
		public static ExtendedLine ExtendedLine(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY,
			Brush brush, DashStyleHelper dashStyle, int width)
		{
			return ExtendedLineCore(owner, false, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, brush, dashStyle, width, false, null);
		}

		/// <summary>
		/// Draws a line with infinite end points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static ExtendedLine ExtendedLine(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY,
			Brush brush, DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ExtendedLineCore(owner, false, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY,
								brush, dashStyle, width, false, null));
		}

		/// <summary>
		/// Draws a line with infinite end points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static ExtendedLine ExtendedLine(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY,
			Brush brush, DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ExtendedLineCore(owner, false, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, brush, dashStyle, width, false, null));
		}

		/// <summary>
		/// Draws a line with infinite end points.
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
		public static ExtendedLine ExtendedLine(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, bool isGlobal, string templateName)
		{
			return ExtendedLineCore(owner, false, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, null,
				DashStyleHelper.Solid, 1, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a line with infinite end points.
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
		public static ExtendedLine ExtendedLine(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY, bool isGlobal, string templateName)
		{
			return ExtendedLineCore(owner, false, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, null,
				DashStyleHelper.Solid, 1, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a line with infinite end points.
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
		public static ExtendedLine ExtendedLine(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime, double endY,
			Brush brush, DashStyleHelper dashStyle, int width)
		{
			return ExtendedLineCore(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, brush, dashStyle, width, false, null);
		}


		/// <summary>
		/// Draws a line with infinite end points.
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
		public static ExtendedLine ExtendedLine(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, double endY,
			Brush brush, DashStyleHelper dashStyle, int width)
		{
			return ExtendedLineCore(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY,
								brush, dashStyle, width, false, null);
		}

		/// <summary>
		/// Draws a line with infinite end points.
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
		public static ExtendedLine ExtendedLine(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, double endY,
			Brush brush, DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ExtendedLineCore(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY,
								brush, dashStyle, width, false, null));
		}

		/// <summary>
		/// Draws a line with infinite end points.
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
		public static ExtendedLine ExtendedLine(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime, double endY,
			Brush brush, DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				ExtendedLineCore(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, brush, dashStyle, width, false, null));
		}


		// horizontal line overloads
		private static HorizontalLine HorizontalLineCore(NinjaScriptBase owner, bool isAutoScale, string tag,
												double y, Brush brush, DashStyleHelper dashStyle, int width)
		{
			return DrawLineTypeCore<HorizontalLine>(owner, isAutoScale, tag, 0, Core.Globals.MinDate, y, 0, Core.Globals.MinDate,
											y, brush, dashStyle, width, false, null);
		}

		/// <summary>
		/// Draws a horizontal line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static HorizontalLine HorizontalLine(NinjaScriptBase owner, string tag, double y, Brush brush)
		{
			return HorizontalLineCore(owner, false, tag, y, brush, DashStyleHelper.Solid, 1);
		}

		/// <summary>
		/// Draws a horizontal line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <returns></returns>
		public static HorizontalLine HorizontalLine(NinjaScriptBase owner, string tag, double y, Brush brush,
													DashStyleHelper dashStyle, int width)
		{
			return HorizontalLineCore(owner, false, tag, y, brush, dashStyle, width);
		}

		/// <summary>
		/// Draws a horizontal line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static HorizontalLine HorizontalLine(NinjaScriptBase owner, string tag, double y, Brush brush, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				HorizontalLineCore(owner, false, tag, y, brush, DashStyleHelper.Solid, 1));
		}

		/// <summary>
		/// Draws a horizontal line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static HorizontalLine HorizontalLine(NinjaScriptBase owner, string tag, double y, Brush brush,
													DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				HorizontalLineCore(owner, false, tag, y, brush, dashStyle, width));
		}

		/// <summary>
		/// Draws a horizontal line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static HorizontalLine HorizontalLine(NinjaScriptBase owner, string tag, double y, bool isGlobal, string templateName)
		{
			return DrawLineTypeCore<HorizontalLine>(owner, false, tag, int.MinValue, Core.Globals.MinDate, y, int.MinValue, Core.Globals.MinDate,
											y, null, DashStyleHelper.Solid, 1, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a horizontal line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <returns></returns>
		public static HorizontalLine HorizontalLine(NinjaScriptBase owner, string tag, bool isAutoScale, double y, Brush brush,
													DashStyleHelper dashStyle, int width)
		{
			return HorizontalLineCore(owner, isAutoScale, tag, y, brush, dashStyle, width);
		}

		/// <summary>
		/// Draws a horizontal line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoscale">if set to <c>true</c> [is autoscale].</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static HorizontalLine HorizontalLine(NinjaScriptBase owner, string tag, bool isAutoscale, double y, Brush brush, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				HorizontalLineCore(owner, isAutoscale, tag, y, brush, DashStyleHelper.Solid, 1));
		}

		// line overloads
		private static Line Line(NinjaScriptBase owner, bool isAutoScale, string tag,
								int startBarsAgo, DateTime startTime, double startY, int endBarsAgo, DateTime endTime, double endY,
								Brush brush, DashStyleHelper dashStyle, int width)
		{
			return DrawLineTypeCore<Line>(owner, isAutoScale, tag, startBarsAgo, startTime, startY, endBarsAgo, endTime, endY, brush, dashStyle, width, false, null);
		}

		/// <summary>
		/// Draws a line between two points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static Line Line(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, Brush brush)
		{
			return Line(owner, false, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, brush, DashStyleHelper.Solid, 1);
		}

		/// <summary>
		/// Draws a line between two points.
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
		public static Line Line(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo,
			double endY, Brush brush, DashStyleHelper dashStyle, int width)
		{
			return Line(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, brush, dashStyle, width);
		}

		/// <summary>
		/// Draws a line between two points.
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
		public static Line Line(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime,
			double endY, Brush brush, DashStyleHelper dashStyle, int width)
		{
			return Line(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, brush, dashStyle, width);
		}

		/// <summary>
		/// Draws a line between two points.
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
		public static Line Line(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo,
			double endY, Brush brush, DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				Line(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, brush, dashStyle, width));
		}

		/// <summary>
		/// Draws a line between two points.
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
		public static Line Line(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime,
			double endY, Brush brush, DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				Line(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, brush, dashStyle, width));
		}

		/// <summary>
		/// Draws a line between two points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static Line Line(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime,
			double endY, string templateName)
		{
			return DrawLineTypeCore<Line>(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY,
				null, DashStyleHelper.Dash, 0, false, templateName);
		}

		/// <summary>
		/// Draws a line between two points.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static Line Line(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo,
			double endY, string templateName)
		{
			return DrawLineTypeCore<Line>(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY,
				null, DashStyleHelper.Dash, 0, false, templateName);
		}

		/// <summary>
		/// Draws a line between two points.
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
		public static Line Line(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo,
			double endY, bool isGlobal, string templateName)
		{
			return DrawLineTypeCore<Line>(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY,
				null, DashStyleHelper.Solid, 0, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a line between two points.
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
		public static Line Line(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime,
			double endY, bool isGlobal, string templateName)
		{
			return DrawLineTypeCore<Line>(owner, isAutoScale, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY,
				null, DashStyleHelper.Solid, 0, isGlobal, templateName);
		}

		// vertical line overloads
		private static VerticalLine VerticalLineCore(NinjaScriptBase owner, bool isAutoScale, string tag,
												int barsAgo, DateTime time, Brush brush, DashStyleHelper dashStyle, int width)
		{
			return DrawLineTypeCore<VerticalLine>(owner, isAutoScale, tag, barsAgo, time, double.MinValue, int.MinValue, Core.Globals.MinDate,
											double.MinValue, brush, dashStyle, width, false, null);
		}

		/// <summary>
		/// Draws a vertical line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static VerticalLine VerticalLine(NinjaScriptBase owner, string tag, DateTime time, Brush brush)
		{
			return VerticalLineCore(owner, false, tag, int.MinValue, time, brush, DashStyleHelper.Solid, 1);
		}

		/// <summary>
		/// Draws a vertical line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <returns></returns>
		public static VerticalLine VerticalLine(NinjaScriptBase owner, string tag, DateTime time, Brush brush,
													DashStyleHelper dashStyle, int width)
		{
			return VerticalLineCore(owner, false, tag, int.MinValue, time, brush, dashStyle, width);
		}

		/// <summary>
		/// Draws a vertical line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static VerticalLine VerticalLine(NinjaScriptBase owner, string tag, int barsAgo, Brush brush)
		{
			return VerticalLineCore(owner, false, tag, barsAgo, Core.Globals.MinDate, brush, DashStyleHelper.Solid, 1);
		}

		/// <summary>
		/// Draws a vertical line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <returns></returns>
		public static VerticalLine VerticalLine(NinjaScriptBase owner, string tag, int barsAgo, Brush brush,
													DashStyleHelper dashStyle, int width)
		{
			return VerticalLineCore(owner, false, tag, barsAgo, Core.Globals.MinDate, brush, dashStyle, width);
		}

		/// <summary>
		/// Draws a vertical line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static VerticalLine VerticalLine(NinjaScriptBase owner, string tag, DateTime time, Brush brush,
													DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				 VerticalLineCore(owner, false, tag, int.MinValue, time, brush, dashStyle, width));
		}

		/// <summary>
		/// Draws a vertical line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static VerticalLine VerticalLine(NinjaScriptBase owner, string tag, int barsAgo, Brush brush,
													DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				VerticalLineCore(owner, false, tag, barsAgo, Core.Globals.MinDate, brush, dashStyle, width));
		}

		/// <summary>
		/// Draws a vertical line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static VerticalLine VerticalLine(NinjaScriptBase owner, string tag, int barsAgo, bool isGlobal, string templateName)
		{
			return DrawLineTypeCore<VerticalLine>(owner, false, tag, barsAgo, Core.Globals.MinDate,
				double.MinValue, int.MinValue, Core.Globals.MinDate, double.MinValue, null, DashStyleHelper.Solid, 1, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a vertical line.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static VerticalLine VerticalLine(NinjaScriptBase owner, string tag, DateTime time, bool isGlobal, string templateName)
		{
			return DrawLineTypeCore<VerticalLine>(owner, false, tag, int.MinValue, time,
				double.MinValue, int.MinValue, Core.Globals.MinDate, double.MinValue, null, DashStyleHelper.Solid, 1, isGlobal, templateName);
		}

		// ray overloads
		private static Ray RayCore(NinjaScriptBase owner, bool isAutoScale, string tag,
								int startBarsAgo, DateTime startTime, double startY, int endBarsAgo, DateTime endTime, double endY,
								Brush brush, DashStyleHelper dashStyle, int width)
		{
			return DrawLineTypeCore<Ray>(owner, isAutoScale, tag, startBarsAgo, startTime, startY, endBarsAgo, endTime, endY, brush, dashStyle, width, false, null);
		}

		/// <summary>
		/// Draws a line which has an infinite end point in one direction.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static Ray Ray(NinjaScriptBase owner, string tag,int startBarsAgo, double startY, int endBarsAgo, double endY, Brush brush)
		{
			return RayCore(owner, false, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, brush, DashStyleHelper.Solid, 1);
		}

		/// <summary>
		/// Draws a line which has an infinite end point in one direction.
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
		public static Ray Ray(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, double endY,
								Brush brush, DashStyleHelper dashStyle, int width)
		{
			return RayCore(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, brush, dashStyle, width);
		}

		/// <summary>
		/// Draws a line which has an infinite end point in one direction.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static Ray Ray(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY, Brush brush)
		{
			return RayCore(owner, false, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, brush, DashStyleHelper.Solid, 1);
		}

		/// <summary>
		/// Draws a line which has an infinite end point in one direction.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <returns></returns>
		public static Ray Ray(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY, Brush brush,
								DashStyleHelper dashStyle, int width)
		{
			return RayCore(owner, false, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, brush, dashStyle, width);
		}

		/// <summary>
		/// Draws a line which has an infinite end point in one direction.
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
		public static Ray Ray(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, double endY,
								Brush brush, DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				RayCore(owner, isAutoScale, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY, brush, dashStyle, width));
		}

		/// <summary>
		/// Draws a line which has an infinite end point in one direction.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="startY">The starting y value coordinate where the draw object will be drawn</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="endY">The end y value coordinate where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object.</param>
		/// <param name="width">The width of the draw object</param>
		/// <param name="drawOnPricePanel">Determines if the draw-object should be on the price panel or a separate panel</param>
		/// <returns></returns>
		public static Ray Ray(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY, Brush brush,
								DashStyleHelper dashStyle, int width, bool drawOnPricePanel)
		{
			return DrawingTool.DrawToggledPricePanel(owner, drawOnPricePanel, () =>
				RayCore(owner, false, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, brush, dashStyle, width));
		}

		/// <summary>
		/// Draws a line which has an infinite end point in one direction.
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
		public static Ray Ray(NinjaScriptBase owner, string tag, int startBarsAgo, double startY, int endBarsAgo, double endY, bool isGlobal, string templateName)
		{
			return DrawLineTypeCore<Ray>(owner, false, tag, startBarsAgo, Core.Globals.MinDate, startY, endBarsAgo, Core.Globals.MinDate, endY,
				null, DashStyleHelper.Solid, 1, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a line which has an infinite end point in one direction.
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
		public static Ray Ray(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY, bool isGlobal, string templateName)
		{
			return DrawLineTypeCore<Ray>(owner, false, tag, int.MinValue, startTime, startY, int.MinValue, endTime, endY, null, DashStyleHelper.Solid, 1, isGlobal, templateName);
		}
	}
}
