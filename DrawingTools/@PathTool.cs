// 
// Copyright (C) 2022, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.DrawingTools
{
	/// <summary>
	/// Represents an interface that exposes information regarding a Path IDrawingTool.
	/// </summary>
	public class PathTool : PathToolSegmentContainer
	{
		[TypeConverter("NinjaTrader.Custom.ResourceEnumConverter")]
		public enum PathToolCapMode
		{
			Arrow,
			Line,
		}

		#region Variables
		private SharpDX.Direct2D1.PathGeometry	arrowPathGeometry;
		private const double					cursorSensitivity		= 15;
		private DispatcherTimer					doubleClickTimer;
		private ChartAnchor						editingAnchor;
		private bool							firstTime				= true;
		#endregion

		#region Properties
		[Browsable(false)]
		[SkipOnCopyTo(true), ExcludeFromTemplate]
		public List<ChartAnchor>					ChartAnchors	{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolTextOutlineStroke",	GroupName = "NinjaScriptGeneral", Order = 0)]
		public Stroke								OutlineStroke	{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolPathBegin",			GroupName = "NinjaScriptGeneral", Order = 1)]
		public PathToolCapMode						PathBegin		{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolPathEnd",			GroupName = "NinjaScriptGeneral", Order = 2)]
		public PathToolCapMode						PathEnd			{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolPathShowCount",		GroupName = "NinjaScriptGeneral", Order = 3)]
		public bool									ShowCount		{ get; set; }

		[Display(Order = 0)]
		[SkipOnCopyTo(true), ExcludeFromTemplate]
		public ChartAnchor							StartAnchor
		{
			get
			{
				if (ChartAnchors == null || ChartAnchors.Count == 0)
					return new ChartAnchor { DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchorStart, IsEditing = true, DrawingTool = this };
				return ChartAnchors[0];
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
		#endregion

		// There always must be a ChartAnchor in here or Path won't even start up
		public override IEnumerable<ChartAnchor> Anchors
		{
			get { return ChartAnchors == null || ChartAnchors.Count == 0 ? new[] { StartAnchor } : ChartAnchors.ToArray(); }
		}

		public override void CopyTo(NinjaScript ninjaScript)
		{
			base.CopyTo(ninjaScript);

			PathTool p = ninjaScript as PathTool;

			if (p != null)
			{
				if (ChartAnchors != null)
				{
					p.ChartAnchors.Clear();
					// We have to deep copy our List of Chart Anchors
					foreach (ChartAnchor ca in ChartAnchors)
						p.ChartAnchors.Add(ca.Clone() as ChartAnchor);
				}
			}
			else // might be in different assembly after compillation
			{
				Type			newInstType			= ninjaScript.GetType();
				PropertyInfo	anchorsPropertyInfo = newInstType.GetProperty("ChartAnchors");

				if (anchorsPropertyInfo == null)
					return;

				IList newAnchors = anchorsPropertyInfo.GetValue(ninjaScript) as IList;

				if (newAnchors == null)
					return;

				// Since new instance could be past set defaults, clear any existing
				newAnchors.Clear();

				foreach (ChartAnchor ca in ChartAnchors)
				{
					try
					{
						ChartAnchor newInstance = ca.Clone() as ChartAnchor;

						if (newInstance == null)
							continue;

						newAnchors.Add(newInstance);
					}
					catch { }
				}
			}
		}

		private SharpDX.Direct2D1.PathGeometry CreatePathGeometry(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, double pixelAdjust)
		{
			List<SharpDX.Vector2>	vectors			= new List<SharpDX.Vector2>();
			Vector					pixelAdjustVec	= new Vector(pixelAdjust, pixelAdjust);

			for (int i = 0; i < ChartAnchors.Count; i++)
			{
				Point p = ChartAnchors[i].GetPoint(chartControl, chartPanel, chartScale);
				vectors.Add((p + pixelAdjustVec).ToVector2());

				if (i + 1 < ChartAnchors.Count)
				{
					Point p2 = ChartAnchors[i + 1].GetPoint(chartControl, chartPanel, chartScale);
					vectors.Add((p2 + pixelAdjustVec).ToVector2());
				}
			}

			SharpDX.Direct2D1.PathGeometry pathGeometry = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
			SharpDX.Direct2D1.GeometrySink geometrySink = pathGeometry.Open();

			geometrySink.BeginFigure(vectors[0], SharpDX.Direct2D1.FigureBegin.Filled);
			geometrySink.AddLines(vectors.ToArray());
			geometrySink.EndFigure(SharpDX.Direct2D1.FigureEnd.Open);
			geometrySink.Close(); // calls dispose for you

			return pathGeometry;
		}

		private void DoubleClickTimerTick(object sender, EventArgs e)
		{
			doubleClickTimer.Stop();
		}

		public override IEnumerable<AlertConditionItem> GetAlertConditionItems()
		{
			if (ChartAnchors == null || ChartAnchors.Count == 0)
				yield break;

			foreach (PathToolSegment segment in PathToolSegments)
			{
				yield return new AlertConditionItem
				{
					Name					= segment.Name,
					ShouldOnlyDisplayName	= true,
					Tag						= segment
				};
			}
		}

		public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
		{
			if (DrawingState == DrawingState.Building)
				return Cursors.Pen;

			if (DrawingState == DrawingState.Moving)
				return IsLocked ? Cursors.No : Cursors.SizeAll;

			if (DrawingState == DrawingState.Editing && IsLocked)
				return Cursors.No;

			if (GetClosestAnchor(chartControl, chartPanel, chartScale, cursorSensitivity, point) == null)
			{
				Point[] pathPoints = GetPathAnchorPoints(chartControl, chartScale);

				if (pathPoints.Length > 0 && (pathPoints.Last() - point).Length <= cursorSensitivity)
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

		private Point[] GetPathAnchorPoints(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel	chartPanel	= chartControl.ChartPanels[PanelIndex];
			Point[]		points		= new Point[ChartAnchors.Count];

			for (int i = 0; i < points.Length; i++)
				points[i] = ChartAnchors[i].GetPoint(chartControl, chartPanel, chartScale);

			return points;
		}

		public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			return GetPathAnchorPoints(chartControl, chartScale);
		}

		public override IEnumerable<Condition> GetValidAlertConditions()
		{
			return new[] { Condition.Less, Condition.LessEqual, Condition.Equals, Condition.Greater, Condition.GreaterEqual, Condition.NotEqual, Condition.CrossAbove, Condition.CrossBelow };
		}
		
		public override object Icon { get { return Icons.DrawPath; } }

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values, ChartControl chartControl, ChartScale chartScale)
		{
			PathToolSegment segAnchors = conditionItem.Tag as PathToolSegment;

			if (segAnchors == null)
				return false;

			ChartPanel	chartPanel		= chartControl.ChartPanels[PanelIndex];
			Point		lineStartPoint	= segAnchors.StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point		lineEndPoint	= segAnchors.EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			double		minLineX		= double.MaxValue;
			double		maxLineX		= double.MinValue;

			foreach (Point point in new[] { lineStartPoint, lineEndPoint })
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
			Point barPoint		= new Point(firstBarX, firstBarY);

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
						double							barX			= chartControl.GetXByTime(v.Time);
						double							barY			= chartScale.GetYByValue(v.Value);
						Point							stepBarPoint	= new Point(barX, barY);
						MathHelper.PointLineLocation	ptLocation		= MathHelper.GetPointLineLocation(leftPoint, rightPoint, stepBarPoint);

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

			float		minX		= float.MaxValue;
			float		maxX		= float.MinValue;
			ChartPanel	chartPanel	= chartControl.ChartPanels[PanelIndex];

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

					ChartAnchor clickTestAnchor = GetClosestAnchor(chartControl, chartPanel, chartScale, cursorSensitivity, p);

					if (ChartAnchors.Count > 1 && doubleClickTimer.IsEnabled && clickTestAnchor != null && clickTestAnchor != ChartAnchors[ChartAnchors.Count - 1])
					{
						// ChartAnchors[ChartAnchors.Count - 1] is the 'temp anchor'
						ChartAnchors.Remove(ChartAnchors[ChartAnchors.Count - 1]);
						PathToolSegments.Remove(PathToolSegments[PathToolSegments.Count - 1]);
						doubleClickTimer.Stop();
						DrawingState	= DrawingState.Normal;
						IsSelected		= false;
					}
					else
					{
						ChartAnchor ca = new ChartAnchor { DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchor, IsEditing = true, DrawingTool = this };

						dataPoint.CopyDataValues(ca);
						ChartAnchors.Add(ca);

						if (ChartAnchors.Count > 1)
						{
							PathToolSegments.Add(new PathToolSegment(ChartAnchors[ChartAnchors.Count - 2], ChartAnchors[ChartAnchors.Count - 1], string.Format("{0} {1}", Custom.Resource.NinjaScriptDrawingToolPathSegment, PathToolSegments.Count + 1)));

							if (!doubleClickTimer.IsEnabled)
								doubleClickTimer.Start();
						}
					}
					break;
				case DrawingState.Normal:
					editingAnchor = GetClosestAnchor(chartControl, chartPanel, chartScale, cursorSensitivity, p);

					if (editingAnchor != null)
					{
						editingAnchor.IsEditing = true;
						DrawingState			= DrawingState.Editing;
					}
					else
					{
						if (GetCursor(chartControl, chartPanel, chartScale, p) != null)
							DrawingState = DrawingState.Moving;
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
				editingAnchor = null;
			}

			DrawingState = DrawingState.Normal;
		}

		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (firstTime && DrawingState == DrawingState.Normal)
			{
				firstTime = false;
				Cbi.License.Log("Path");
			}

			RenderTarget.AntialiasMode						= SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			Stroke							outlineStroke	= OutlineStroke;
			outlineStroke.RenderTarget						= RenderTarget;
			ChartPanel						chartPanel		= chartControl.ChartPanels[PanelIndex];
			double							strokePixAdjust	= outlineStroke.Width % 2 == 0 ? 0.5d : 0d;
			Vector							pixelAdjustVec	= new Vector(strokePixAdjust, strokePixAdjust);
			SharpDX.Direct2D1.PathGeometry	polyGeo			= CreatePathGeometry(chartControl, chartPanel, chartScale, strokePixAdjust);
			SharpDX.Direct2D1.Brush			tmpBrush		= IsInHitTest ? chartControl.SelectionBrush : outlineStroke.BrushDX;

			RenderTarget.DrawGeometry(polyGeo, tmpBrush, outlineStroke.Width, outlineStroke.StrokeStyle);
			polyGeo.Dispose();

			if (PathBegin == PathToolCapMode.Arrow || PathEnd == PathToolCapMode.Arrow)
			{
				Point[] points = GetPathAnchorPoints(chartControl, chartScale);

				if (points.Length > 1)
				{
					if (arrowPathGeometry == null)
					{
						arrowPathGeometry								= new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
						SharpDX.Direct2D1.GeometrySink	geometrySink	= arrowPathGeometry.Open();
						float							arrowWidth		= 6f;
						SharpDX.Vector2					top				= new SharpDX.Vector2(0, outlineStroke.Width * 0.5f);

						geometrySink.BeginFigure(top, SharpDX.Direct2D1.FigureBegin.Filled);
						geometrySink.AddLine(new SharpDX.Vector2(arrowWidth, -arrowWidth));
						geometrySink.AddLine(new SharpDX.Vector2(-arrowWidth, -arrowWidth));
						geometrySink.AddLine(top);// cap off figure
						geometrySink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
						geometrySink.Close();
					}

					if (PathBegin == PathToolCapMode.Arrow)
					{
						Vector lineVector = points[0] - points[1];

						lineVector.Normalize();

						Point				pointAdjusted		= points[0] + pixelAdjustVec;
						SharpDX.Vector2		pointVec			= pointAdjusted.ToVector2();
						float				vectorAngle			= -(float)Math.Atan2(lineVector.X, lineVector.Y);
						Vector				adjustVector		= lineVector * 5;
						SharpDX.Vector2		arrowPointVec		= new SharpDX.Vector2((float)(pointVec.X + adjustVector.X), (float)(pointVec.Y + adjustVector.Y));
						SharpDX.Matrix3x2	transformMatrix2	= SharpDX.Matrix3x2.Rotation(vectorAngle, SharpDX.Vector2.Zero) * SharpDX.Matrix3x2.Scaling((float)Math.Max(1.0f, outlineStroke.Width * .45) + 0.25f) * SharpDX.Matrix3x2.Translation(arrowPointVec);
						RenderTarget.Transform					= transformMatrix2;

						RenderTarget.FillGeometry(arrowPathGeometry, tmpBrush);
						RenderTarget.Transform = SharpDX.Matrix3x2.Identity;
					}

					if (PathEnd == PathToolCapMode.Arrow)
					{
						Vector lineVector = points[points.Length - 1] - points[points.Length - 2];

						lineVector.Normalize();

						Point				pointAdjusted		= points[points.Length - 1] + pixelAdjustVec;
						SharpDX.Vector2		pointVec			= pointAdjusted.ToVector2();
						float				vectorAngle			= -(float)Math.Atan2(lineVector.X, lineVector.Y);
						Vector				adjustVector		= lineVector * 5;
						SharpDX.Vector2		arrowPointVec		= new SharpDX.Vector2((float)(pointVec.X + adjustVector.X), (float)(pointVec.Y + adjustVector.Y));
						SharpDX.Matrix3x2	transformMatrix2	= SharpDX.Matrix3x2.Rotation(vectorAngle, SharpDX.Vector2.Zero) * SharpDX.Matrix3x2.Scaling((float)Math.Max(1.0f, outlineStroke.Width * .45) + 0.25f) * SharpDX.Matrix3x2.Translation(arrowPointVec);
						RenderTarget.Transform					= transformMatrix2;

						RenderTarget.FillGeometry(arrowPathGeometry, tmpBrush);

						RenderTarget.Transform = SharpDX.Matrix3x2.Identity;
					}
				}
			}

			if (ShowCount)
			{
				SimpleFont						wpfFont		= chartControl.Properties.LabelFont ?? new SimpleFont();
				SharpDX.DirectWrite.TextFormat	textFormat	= wpfFont.ToDirectWriteTextFormat();
				textFormat.TextAlignment					= SharpDX.DirectWrite.TextAlignment.Leading;
				textFormat.WordWrapping						= SharpDX.DirectWrite.WordWrapping.NoWrap;

				for (int i = 1; i < ChartAnchors.Count; i++)
				{
					Point p		= ChartAnchors[i - 1].GetPoint(chartControl, chartPanel, chartScale);
					Point p1	= ChartAnchors[i].GetPoint(chartControl, chartPanel, chartScale);

					if (i + 1 < ChartAnchors.Count)
					{
						Point	p2	= ChartAnchors[i + 1].GetPoint(chartControl, chartPanel, chartScale);
						Vector	v1	= p - p1;

						v1.Normalize();

						Vector v2 = p2 - p1;

						v2.Normalize();

						Vector vector = v1 + v2;

						vector.Normalize();

						SharpDX.DirectWrite.TextLayout	textLayout	= new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, i.ToString(), textFormat, 250, textFormat.FontSize);
						Point							textPoint	= p1 - vector * textFormat.FontSize;
						textPoint.X									-= textLayout.Metrics.Width / 2f;
						textPoint.Y									-= textLayout.Metrics.Height / 2f;

						RenderTarget.DrawTextLayout((textPoint + pixelAdjustVec).ToVector2(), textLayout, outlineStroke.BrushDX, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
						textLayout.Dispose();
					}
					else
					{
						SharpDX.DirectWrite.TextLayout	textLayout	= new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, i.ToString(), textFormat, 250, textFormat.FontSize);
						Vector							vector		= (p - p1);

						vector.Normalize();

						Point textPoint = p1 - vector * textFormat.FontSize;
						textPoint.X		-= textLayout.Metrics.Width / 2f;
						textPoint.Y		-= textLayout.Metrics.Height / 2f;

						RenderTarget.DrawTextLayout((textPoint + pixelAdjustVec).ToVector2(), textLayout, outlineStroke.BrushDX, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
						textLayout.Dispose();
					}
				}

				textFormat.Dispose();
			}
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				DrawingState	= DrawingState.Building;
				Name			= Custom.Resource.NinjaScriptDrawingToolPath;
				OutlineStroke	= new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Solid, 2, 100);
				ChartAnchors	= new List<ChartAnchor>();
				PathBegin		= PathToolCapMode.Line;
				PathEnd			= PathToolCapMode.Line;
				ShowCount		= false;
			}
			else if (State == State.Active)
			{
				if (doubleClickTimer == null)
					doubleClickTimer = new DispatcherTimer(new TimeSpan(0, 0, 0, 0, System.Windows.Forms.SystemInformation.DoubleClickTime), DispatcherPriority.Background, DoubleClickTimerTick, Dispatcher.CurrentDispatcher);
			}
		}

		public override bool SupportsAlerts { get { return true; } }
	}

	public class PathToolSegment : ICloneable
	{
		[Browsable(false)]
		public ChartAnchor	EndAnchor	{ get; set; }

		[Browsable(false)]
		public string		Name		{ get; set; }

		[Browsable(false)]
		public ChartAnchor	StartAnchor { get; set; }

		public object AssemblyClone(Type t)
		{
			Assembly a				= t.Assembly;
			object pathToolSegment	= a.CreateInstance(t.FullName);

			foreach (PropertyInfo p in t.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
			{
				if (p.CanWrite)
					p.SetValue(pathToolSegment, GetType().GetProperty(p.Name).GetValue(this), null);
			}

			return pathToolSegment;
		}

		public virtual object Clone()
		{
			PathToolSegment newSeg = new PathToolSegment();

			CopyTo(newSeg);
			return newSeg;
		}

		public virtual void CopyTo(PathToolSegment other)
		{
			StartAnchor.CopyDataValues(other.StartAnchor);
			EndAnchor.CopyDataValues(other.EndAnchor);

			other.Name = Name;
		}

		public PathToolSegment()
		{
			StartAnchor = new ChartAnchor();
			EndAnchor	= new ChartAnchor();
			Name		= string.Empty;
		}

		public PathToolSegment(ChartAnchor startAnchor, ChartAnchor endAnchor, string name)
		{
			StartAnchor = startAnchor;
			EndAnchor	= endAnchor;
			Name		= name;
		}
	}

	public abstract class PathToolSegmentContainer : DrawingTool
	{
		[Browsable(false)]
		[SkipOnCopyTo(true)]
		public List<PathToolSegment> PathToolSegments { get; set; }

		public override void CopyTo(NinjaScript ninjaScript)
		{
			base.CopyTo(ninjaScript);

			Type			newInstType			= ninjaScript.GetType();
			PropertyInfo	segmentPropertyInfo = newInstType.GetProperty("PathToolSegments");

			if (segmentPropertyInfo == null)
				return;

			IList newInstPathToolSegments = segmentPropertyInfo.GetValue(ninjaScript) as IList;

			if (newInstPathToolSegments == null)
				return;

			// Since new instance could be past set defaults, clear any existing
			newInstPathToolSegments.Clear();

			foreach (PathToolSegment oldPathToolSegment in PathToolSegments)
			{
				try
				{
					object newInstance = oldPathToolSegment.AssemblyClone(ninjaScript.GetType().Assembly.GetType(typeof(PathToolSegment).FullName));

					if (newInstance == null)
						continue;

					newInstPathToolSegments.Add(newInstance);
				}
				catch (ArgumentException)
				{
					// In compiled assembly case, Add call will fail for different assemblies so do normal clone instead
					object newInstance = oldPathToolSegment.Clone();

					if (newInstance == null)
						continue;

					newInstPathToolSegments.Add(newInstance);
				}
				catch { }
			}
		}

		protected PathToolSegmentContainer()
		{
			PathToolSegments = new List<PathToolSegment>();
		}
	}

	public static partial class Draw
	{
		private static PathTool PathCore(NinjaScriptBase owner, string tag, bool isAutoScale, List<ChartAnchor> chartAnchors, Brush brush, DashStyleHelper dashStyle, bool isGlobal, string templateName)
		{
			if (owner == null)
				throw new ArgumentException("owner");

			if (string.IsNullOrWhiteSpace(tag))
				throw new ArgumentException(@"tag cant be null or empty", "tag");

			if (isGlobal && tag[0] != GlobalDrawingToolManager.GlobalDrawingToolTagPrefix)
				tag = GlobalDrawingToolManager.GlobalDrawingToolTagPrefix + tag;

			PathTool path = DrawingTool.GetByTagOrNew(owner, typeof(PathTool), tag, templateName) as PathTool;

			if (path == null)
				return null;

			DrawingTool.SetDrawingToolCommonValues(path, tag, isAutoScale, owner, isGlobal);

			if (chartAnchors != null)
			{
				path.ChartAnchors = chartAnchors;

				for (int i = 1; i < chartAnchors.Count; i++)
					path.PathToolSegments.Add(new PathToolSegment(chartAnchors[i - 1], chartAnchors[i], string.Format("{0} {1}", Custom.Resource.NinjaScriptDrawingToolPathSegment, i)));
			}

			if (brush != null)
				path.OutlineStroke = new Stroke(brush, dashStyle, 2);

			path.SetState(State.Active);
			return path;
		}

		private static PathTool PathBasic(NinjaScriptBase owner, string tag, bool isAutoScale,
			int anchor1BarsAgo, DateTime anchor1Time, double anchor1Y, int anchor2BarsAgo, DateTime anchor2Time, double anchor2Y,
			int anchor3BarsAgo, DateTime anchor3Time, double anchor3Y, int anchor4BarsAgo, DateTime anchor4Time, double anchor4Y,
			int anchor5BarsAgo, DateTime anchor5Time, double anchor5Y)
		{
			List<ChartAnchor> chartAnchors = new List<ChartAnchor>
			{
				DrawingTool.CreateChartAnchor(owner, anchor1BarsAgo, anchor1Time, anchor1Y),
				DrawingTool.CreateChartAnchor(owner, anchor2BarsAgo, anchor2Time, anchor2Y),
				DrawingTool.CreateChartAnchor(owner, anchor3BarsAgo, anchor3Time, anchor3Y)
			};

			if (anchor4BarsAgo != int.MinValue || anchor4Time != DateTime.MinValue)
				chartAnchors.Add(DrawingTool.CreateChartAnchor(owner, anchor4BarsAgo, anchor4Time, anchor4Y));

			if (anchor5BarsAgo != int.MinValue || anchor5Time != DateTime.MinValue)
				chartAnchors.Add(DrawingTool.CreateChartAnchor(owner, anchor5BarsAgo, anchor5Time, anchor5Y));

			return PathCore(owner, tag, isAutoScale, chartAnchors, null, DashStyleHelper.Solid, false, string.Empty);
		}

		/// <summary>
		/// Draws a Path.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="anchor1BarsAgo">The number of bars ago (x axis coordinate) to draw the first anchor point</param>
		/// <param name="anchor1Y">The y value coordinate of the first anchor point</param>
		/// <param name="anchor2BarsAgo">The number of bars ago (x axis coordinate) to draw the second anchor point</param>
		/// <param name="anchor2Y">The y value coordinate of the second anchor point</param>
		/// <param name="anchor3BarsAgo">The number of bars ago (x axis coordinate) to draw the third anchor point</param>
		/// <param name="anchor3Y">The y value coordinate of the third anchor point</param>
		/// <returns></returns>
		public static PathTool PathTool(NinjaScriptBase owner, string tag, bool isAutoScale, int anchor1BarsAgo, double anchor1Y, int anchor2BarsAgo, double anchor2Y, int anchor3BarsAgo, double anchor3Y)
		{
			return PathBasic(owner, tag, isAutoScale, anchor1BarsAgo, DateTime.MinValue, anchor1Y, anchor2BarsAgo, DateTime.MinValue, anchor2Y, anchor3BarsAgo, DateTime.MinValue, anchor3Y, int.MinValue, DateTime.MinValue, double.MinValue, int.MinValue, DateTime.MinValue, double.MinValue);
		}

		/// <summary>
		/// Draws a Path.
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
		/// <returns></returns>
		public static PathTool PathTool(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime anchor1Time, double anchor1Y, DateTime anchor2Time, double anchor2Y, DateTime anchor3Time, double anchor3Y)
		{
			return PathBasic(owner, tag, isAutoScale, int.MinValue, anchor1Time, anchor1Y, int.MinValue, anchor2Time, anchor2Y, int.MinValue, anchor3Time, anchor3Y, int.MinValue, DateTime.MinValue, double.MinValue, int.MinValue, DateTime.MinValue, double.MinValue);
		}

		/// <summary>
		/// Draws a Path.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="anchor1BarsAgo">The number of bars ago (x axis coordinate) to draw the first anchor point</param>
		/// <param name="anchor1Y">The y value coordinate of the first anchor point</param>
		/// <param name="anchor2BarsAgo">The number of bars ago (x axis coordinate) to draw the second anchor point</param>
		/// <param name="anchor2Y">The y value coordinate of the second anchor point</param>
		/// <param name="anchor3BarsAgo">The number of bars ago (x axis coordinate) to draw the third anchor point</param>
		/// <param name="anchor3Y">The y value coordinate of the third anchor point</param>
		/// <param name="anchor4BarsAgo">The number of bars ago (x axis coordinate) to draw the fourth anchor point</param>
		/// <param name="anchor4Y">The y value coordinate of the fourth anchor point</param>
		/// <returns></returns>
		public static PathTool PathTool(NinjaScriptBase owner, string tag, bool isAutoScale, int anchor1BarsAgo, double anchor1Y, int anchor2BarsAgo, double anchor2Y, int anchor3BarsAgo, double anchor3Y, int anchor4BarsAgo, double anchor4Y)
		{
			return PathBasic(owner, tag, isAutoScale, anchor1BarsAgo, DateTime.MinValue, anchor1Y, anchor2BarsAgo, DateTime.MinValue, anchor2Y, anchor3BarsAgo, DateTime.MinValue, anchor3Y, anchor4BarsAgo, DateTime.MinValue, anchor4Y, int.MinValue, DateTime.MinValue, double.MinValue);
		}

		/// <summary>
		/// Draws a Path.
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
		public static PathTool PathTool(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime anchor1Time, double anchor1Y, DateTime anchor2Time, double anchor2Y, DateTime anchor3Time, double anchor3Y, DateTime anchor4Time, double anchor4Y)
		{
			return PathBasic(owner, tag, isAutoScale, int.MinValue, anchor1Time, anchor1Y, int.MinValue, anchor2Time, anchor2Y, int.MinValue, anchor3Time, anchor3Y, int.MinValue, anchor4Time, anchor4Y, int.MinValue, DateTime.MinValue, double.MinValue);
		}

		/// <summary>
		/// Draws a Path.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="anchor1BarsAgo">The number of bars ago (x axis coordinate) to draw the first anchor point</param>
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
		public static PathTool PathTool(NinjaScriptBase owner, string tag, bool isAutoScale, int anchor1BarsAgo, double anchor1Y, int anchor2BarsAgo, double anchor2Y, int anchor3BarsAgo, double anchor3Y, int anchor4BarsAgo, double anchor4Y, int anchor5BarsAgo, double anchor5Y)
		{
			return PathBasic(owner, tag, isAutoScale, anchor1BarsAgo, DateTime.MinValue, anchor1Y, anchor2BarsAgo, DateTime.MinValue, anchor2Y, anchor3BarsAgo, DateTime.MinValue, anchor3Y, anchor4BarsAgo, DateTime.MinValue, anchor4Y, anchor5BarsAgo, DateTime.MinValue, anchor5Y);
		}

		/// <summary>
		/// Draws a Path.
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
		public static PathTool PathTool(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime anchor1Time, double anchor1Y, DateTime anchor2Time, double anchor2Y, DateTime anchor3Time, double anchor3Y, DateTime anchor4Time, double anchor4Y, DateTime anchor5Time, double anchor5Y)
		{
			return PathBasic(owner, tag, isAutoScale, int.MinValue, anchor1Time, anchor1Y, int.MinValue, anchor2Time, anchor2Y, int.MinValue, anchor3Time, anchor3Y, int.MinValue, anchor4Time, anchor4Y, int.MinValue, anchor5Time, anchor5Y);
		}

		/// <summary>
		/// Draws a ath.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="chartAnchors">A List of ChartAnchor objects defining the path</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <param name="dashStyle">The dash style used for the lines of the object</param>
		/// <returns></returns>
		public static PathTool PathTool(NinjaScriptBase owner, string tag, bool isAutoScale, List<ChartAnchor> chartAnchors, Brush brush, DashStyleHelper dashStyle)
		{
			return PathCore(owner, tag, isAutoScale, chartAnchors, brush, dashStyle, false, string.Empty);
		}

		/// <summary>
		/// Draws a Path.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="chartAnchors">A List of ChartAnchor objects defining the path</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static PathTool PathTool(NinjaScriptBase owner, string tag, bool isAutoScale, List<ChartAnchor> chartAnchors, bool isGlobal, string templateName)
		{
			return PathCore(owner, tag, isAutoScale, chartAnchors, null, DashStyleHelper.Solid, isGlobal, templateName);
		}
	}
}