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
using System.Xml.Serialization;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.DrawingTools
{
	/// <summary>
	/// Represents an interface that exposes information regarding a Gann Fan IDrawingTool.
	/// </summary>
	public class GannFan : GannAngleContainer
	{
		[TypeConverter("NinjaTrader.Custom.ResourceEnumConverter")]
		public enum GannFanDirection
		{
			UpLeft,
			UpRight,
			DownLeft,
			DownRight
		}

		public	ChartAnchor		Anchor { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolGannFanFanDirection", GroupName = "NinjaScriptGeneral", Order = 3)]
		public GannFanDirection	FanDirection { get; set; }

		public override object Icon { get { return Icons.DrawGanFan; } }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolGannFanDisplayText", GroupName = "NinjaScriptGeneral", Order = 2)]
		public bool IsTextDisplayed { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolGannFanPointsPerBar", GroupName = "NinjaScriptGeneral", Order = 4)]
		public double PointsPerBar { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolPriceLevelsOpacity", GroupName = "NinjaScriptGeneral")]
		public int PriceLevelOpacity { get; set; }

		public override IEnumerable<ChartAnchor> Anchors { get { return new[] { Anchor }; } }

		public override bool SupportsAlerts { get { return true; } }

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;

			if (!IsVisible)
				return;

			if (Anchor.IsEditing)
				return;

			// dont autoscale gann angle extensions
			MinValue = MaxValue = Anchor.Price;
		}

		public Point CalculateExtendedDataPoint(ChartPanel panel, ChartScale scale, int startX, double startPrice, Vector slope)
		{
			// find direction of slope
			bool	right		= slope.X > 0;
			bool	up			= slope.Y > 0;
			int		xLength 	= right ? panel.W - startX : panel.X + startX;
			double	priceSize 	= Math.Abs(xLength / slope.X) * slope.Y;
			double	endY 		= startPrice + priceSize;
			double	maxY 		= up ? panel.MaxValue : panel.MinValue;
			// check if Y endpoint is outside top or bottom of panel
			if (up ? endY > maxY : maxY > endY)
			{
				double yLength 	= Math.Abs(maxY - startPrice);
				double xSize 	= Math.Abs(yLength / slope.Y) * slope.X;
				double endX 	= startX + xSize;
				return new Point(endX, scale.GetYByValue(maxY));
			}
			else
			{
				return new Point(right ? panel.W : 0, scale.GetYByValue(endY));
			}
		}

		public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
		{
			if (DrawingState == DrawingState.Building)
				return Cursors.Pen;
			if (DrawingState == DrawingState.Moving)
				return IsLocked ? Cursors.No : Cursors.SizeAll;

			Point anchorPointPixels = Anchor.GetPoint(chartControl, chartPanel, chartScale);
			Vector vecToMouse = point - anchorPointPixels;
			if (DrawingState == DrawingState.Editing || vecToMouse.Length <= 10)
			{
				if (IsLocked)
					return DrawingState == DrawingState.Editing ? Cursors.No : Cursors.Arrow;
				return Cursors.SizeNESW;
			}

			// check extended lines
			foreach (Point endPoint in GetGannEndPoints(chartControl, chartScale))
			{
				Vector gannVector = endPoint - anchorPointPixels;
				if (MathHelper.IsPointAlongVector(point, anchorPointPixels, gannVector, 10))
				{
					if (IsLocked)
						return DrawingState == DrawingState.Editing ? Cursors.No : Cursors.Arrow;
					return Cursors.SizeAll;
				}
			}
			return null;
		}

		public override IEnumerable<AlertConditionItem> GetAlertConditionItems()
		{
			if (GannAngles == null)
				yield break;
			foreach (GannAngle ga in GannAngles)
				yield return new AlertConditionItem
				{
					Name					= ga.Name,
					Tag						= ga,
					ShouldOnlyDisplayName	= true,
				};
		}

		private IEnumerable<Point> GetGannEndPoints(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel panel	= chartControl.ChartPanels[PanelIndex];
			Point anchorPoint	= Anchor.GetPoint(chartControl, panel, chartScale);
			foreach (GannAngle gannAngle in GannAngles.Where(ga => ga.IsVisible))
			{
				double dx				= gannAngle.RatioX * chartControl.Properties.BarDistance;
				double dVal				= gannAngle.RatioY * PointsPerBar;
				Point stepPoint			= GetGannStepPoint(chartScale, anchorPoint.X, Anchor.Price, dx, dVal);
				Point extendedEndPoint	= GetExtendedPoint(anchorPoint, stepPoint);
				yield return new Point(Math.Max(extendedEndPoint.X, 1), Math.Max(extendedEndPoint.Y, 1));
			}
		}

		private Point GetGannStepPoint(ChartScale scale, double startX, double startPrice, double deltaX, double deltaPrice)
		{
			double x;
			double price;
			switch (FanDirection)
			{
				// note: price operations are backwards because 'up' is higher prices / lower Y value
				case GannFanDirection.DownLeft:		x = startX - deltaX; price = startPrice - deltaPrice;	break;
				case GannFanDirection.DownRight:	x = startX + deltaX; price = startPrice - deltaPrice;	break;
				case GannFanDirection.UpLeft:		x = startX - deltaX; price = startPrice + deltaPrice;	break;
				default:							x = startX + deltaX; price = startPrice + deltaPrice;	break;
			}
			return new Point(x, scale.GetYByValue(price));
		}

		private Vector GetGannStepDataVector(double deltaX, double deltaPrice)
		{
			switch (FanDirection)
			{
				case GannFanDirection.DownLeft:		return new Vector(-deltaX,				-deltaPrice);
				case GannFanDirection.DownRight:	return new Vector(Math.Abs(deltaX),		-deltaPrice);
				case GannFanDirection.UpLeft:		return new Vector(-deltaX,				Math.Abs(deltaPrice));
				default:							return new Vector(Math.Abs(deltaX),		Math.Abs(deltaPrice));
			}
		}

		public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel	chartPanel 	= chartControl.ChartPanels[chartScale.PanelIndex];
			Point		anchorPoint = Anchor.GetPoint(chartControl, chartPanel, chartScale);
			return new[] { anchorPoint };
		}

		public override IEnumerable<Condition> GetValidAlertConditions()
		{
			return new[] { Condition.NotEqual, Condition.Equals, Condition.LessEqual, Condition.Less, Condition.GreaterEqual, Condition.Greater, Condition.CrossAbove, Condition.CrossBelow };
		}

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values, ChartControl chartControl, ChartScale chartScale)
		{
			GannAngle gannAngle = conditionItem.Tag as GannAngle;
			if (gannAngle == null)
				return false;

			ChartPanel	chartPanel	= chartControl.ChartPanels[PanelIndex];
			Point		anchorPoint	= Anchor.GetPoint(chartControl, chartPanel, chartScale);

			// dig out the line we're running on 
			double dx				= gannAngle.RatioX * chartControl.Properties.BarDistance;
			double dVal				= chartScale.GetPixelsForDistance(gannAngle.RatioY * chartControl.Instrument.MasterInstrument.TickSize);
			Point stepPoint			= GetGannStepPoint(chartScale, anchorPoint.X, Anchor.Price, dx, dVal);
			Point extendedEndPoint	= GetExtendedPoint(anchorPoint, stepPoint);

			if (values[0].ValueType == ChartAlertValueType.StaticTime)
			{
				int checkX = chartControl.GetXByTime(values[0].Time);
				return stepPoint.X >= checkX || stepPoint.X >= checkX;
			}

			double barX = chartControl.GetXByTime(values[0].Time);
			double barY = chartScale.GetYByValue(values[0].Value);
			Point barPoint = new Point(barX, barY);

			// bars passed our drawing tool line
			if (extendedEndPoint.X < barX)
				return false;

			// bars not yet to our drawing tool line
			if (stepPoint.X > barY)
				return false;

			if (condition == Condition.CrossAbove || condition == Condition.CrossBelow)
			{
				Predicate<ChartAlertValue> predicate = v =>
				{
					// bar x/y
					double bX = chartControl.GetXByTime(v.Time);
					double bY = chartScale.GetYByValue(v.Value);
					Point stepBarPoint = new Point(bX, bY);
					// NOTE: 'left / right' is relative to if line was vertical. it can end up backwards too
					MathHelper.PointLineLocation ptLocation = MathHelper.GetPointLineLocation(anchorPoint, extendedEndPoint, stepBarPoint);
					if (condition == Condition.CrossAbove)
						return ptLocation == MathHelper.PointLineLocation.LeftOrAbove;
					return ptLocation == MathHelper.PointLineLocation.RightOrBelow;
				};
				return MathHelper.DidPredicateCross(values, predicate);
			}


			MathHelper.PointLineLocation pointLocation = MathHelper.GetPointLineLocation(anchorPoint, extendedEndPoint, barPoint);
			switch (condition)
			{
				case Condition.Greater:			return pointLocation == MathHelper.PointLineLocation.LeftOrAbove;
				case Condition.GreaterEqual:	return pointLocation == MathHelper.PointLineLocation.LeftOrAbove || pointLocation == MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.Less:			return pointLocation == MathHelper.PointLineLocation.RightOrBelow;
				case Condition.LessEqual:		return pointLocation == MathHelper.PointLineLocation.RightOrBelow || pointLocation == MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.Equals:			return pointLocation == MathHelper.PointLineLocation.DirectlyOnLine;
				case Condition.NotEqual:		return pointLocation != MathHelper.PointLineLocation.DirectlyOnLine;
				default:						return false;
			}
		}

		public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			if (DrawingState == DrawingState.Building)
				return true;

			// if the anchor is in times, we're done
			if (Anchor.Time >= firstTimeOnChart && Anchor.Time <= lastTimeOnChart)
				return true;

			// NT7 logic below

			// left hemisphere?
			if (Anchor.Time > lastTimeOnChart && (FanDirection == GannFanDirection.DownLeft || FanDirection == GannFanDirection.UpLeft))
				return true;

			// right hemisphere?
			if (Anchor.Time < firstTimeOnChart && (FanDirection == GannFanDirection.DownRight || FanDirection == GannFanDirection.UpRight))
				return true;

			return false;
		}

		public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:
					if (PointsPerBar < 0)
						PointsPerBar = AttachedTo.Instrument.MasterInstrument.TickSize;
					dataPoint.CopyDataValues(Anchor);
					Anchor.IsEditing = false;
					DrawingState = DrawingState.Normal;
					IsSelected = false;
					break;
				case DrawingState.Normal:
					// make sure they clicked near our anchor or a gann line
					Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
					if (GetClosestAnchor(chartControl, chartPanel, chartScale, 10, point) == Anchor)
						DrawingState = DrawingState.Editing;
					else if (GetCursor(chartControl, chartControl.ChartPanels[PanelIndex], chartScale, point) == Cursors.SizeAll)
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
			
			if (DrawingState == DrawingState.Editing)
				dataPoint.CopyDataValues(Anchor);
			else if (DrawingState == DrawingState.Moving)
				Anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
		}

		public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			DrawingState = DrawingState.Normal;
		}

		protected override void OnStateChange()
		{
			switch (State)
			{
				case State.SetDefaults:
					Description			= Custom.Resource.NinjaScriptDrawingToolGannFan;
					Name				= Custom.Resource.NinjaScriptDrawingToolGannFan;
					Anchor = new ChartAnchor
					{
						DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchor,
						IsEditing		= true,
					};
					FanDirection		= GannFanDirection.UpRight;
					PriceLevelOpacity	= 5;
					IsTextDisplayed		= true;
					PointsPerBar		= -1;
					break;
				case State.Configure:
					if (GannAngles.Count == 0)
					{
						Brush[] gannBrushes = { Brushes.Red, Brushes.MediumOrchid, Brushes.DarkSlateBlue, Brushes.SteelBlue,
												Brushes.Gray, Brushes.MediumAquamarine, Brushes.Khaki, Brushes.Coral, Brushes.Red };
						for (int i = 0; i < 9; i++)
						{
							int ratioX = i == 8 ? 8 : (i <= 4 ? 1 : i - 3);
							int ratioY = i == 0 ? 8 : (i <= 4 ? 5 - i : 1);
							GannAngles.Add(new GannAngle(ratioX, ratioY, gannBrushes[i % 8]));
						}
					}
					break;
				case State.Terminated:
					Dispose();
					break;
			}
		}

		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			RenderTarget.AntialiasMode	= SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

			ChartPanel	panel			= chartControl.ChartPanels[PanelIndex];
			Point		anchorPoint		= Anchor.GetPoint(chartControl, panel, chartScale);

			Point lastEndPoint = new Point(0, 0);
			SharpDX.Direct2D1.Brush lastBrush = null;
			foreach (GannAngle gannAngle in GannAngles.Where(ga => ga.IsVisible && ga.Stroke != null).OrderBy(ga => (ga.RatioX / ga.RatioY)))
			{
				gannAngle.Stroke.RenderTarget = RenderTarget;

				double	dx					= gannAngle.RatioX * chartControl.Properties.BarDistance;
				double	dVal				= gannAngle.RatioY * PointsPerBar;//NT7, just multiple directly this is price not pixels //chartScale.GetPixelsForDistance(gannAngle.RatioY * PointsPerBar);
				Vector	gannDataVector		= GetGannStepDataVector(dx, dVal);
				Point	extendedEndPoint	= CalculateExtendedDataPoint(panel, chartScale, Convert.ToInt32(anchorPoint.X), Anchor.Price, gannDataVector);

				// align to full pixel to avoid unneeded aliasing
				double strokePixAdj		=	((double)(gannAngle.Stroke.Width % 2)).ApproxCompare(0) == 0 ? 0.5d : 0d;
				Vector pixelAdjustVec	= new Vector(0, strokePixAdj);

				SharpDX.Direct2D1.Brush tmpBrush = IsInHitTest ? chartControl.SelectionBrush : gannAngle.Stroke.BrushDX;
				RenderTarget.DrawLine((anchorPoint + pixelAdjustVec).ToVector2(), (extendedEndPoint + pixelAdjustVec).ToVector2(), tmpBrush, gannAngle.Stroke.Width, gannAngle.Stroke.StrokeStyle);

				if (lastBrush != null)
				{
					float oldOpacity = lastBrush.Opacity;
					lastBrush.Opacity = PriceLevelOpacity / 100f;

					// create geometry
					SharpDX.Direct2D1.PathGeometry lineGeometry = new SharpDX.Direct2D1.PathGeometry(Core.Globals.D2DFactory);
					SharpDX.Direct2D1.GeometrySink sink = lineGeometry.Open();
					sink.BeginFigure(lastEndPoint.ToVector2(), SharpDX.Direct2D1.FigureBegin.Filled);
					// Does the fill color need to fill a corner?  Check and add a point
					if (Math.Abs(lastEndPoint.Y - extendedEndPoint.Y) > 0.1 && Math.Abs(lastEndPoint.X - extendedEndPoint.X) > 0.1)
					{
						double boundaryX;
						double boundaryY;

						if (lastEndPoint.Y <= ChartPanel.Y || lastEndPoint.Y >= ChartPanel.Y + ChartPanel.H)
						{
							if (FanDirection == GannFanDirection.UpLeft || FanDirection == GannFanDirection.UpRight)
							{
								boundaryY = extendedEndPoint.Y;
								boundaryX = lastEndPoint.X;
							}
							else
							{
								boundaryY = lastEndPoint.Y;
								boundaryX = extendedEndPoint.X;
							}
						}
						else
						{
							if (FanDirection == GannFanDirection.UpLeft || FanDirection == GannFanDirection.UpRight)
							{
								boundaryY = lastEndPoint.Y;
								boundaryX = extendedEndPoint.X;
							}
							else
							{
								boundaryY = extendedEndPoint.Y;
								boundaryX = lastEndPoint.X;
							}
						}
						sink.AddLine(new SharpDX.Vector2((float)boundaryX, (float)boundaryY));
					}

					sink.AddLine(extendedEndPoint.ToVector2());
					sink.AddLine((anchorPoint + pixelAdjustVec).ToVector2());
					sink.AddLine((lastEndPoint).ToVector2());
					sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
					sink.Close();
					RenderTarget.FillGeometry(lineGeometry, lastBrush);
					lineGeometry.Dispose();

					lastBrush.Opacity = oldOpacity;
				}
				lastEndPoint = extendedEndPoint + pixelAdjustVec;
				lastBrush = tmpBrush;
			}

			if (!IsTextDisplayed || IsInHitTest)
				return;

			foreach (GannAngle gannAngle in GannAngles.Where(ga => ga.IsVisible && ga.Stroke != null).OrderBy(ga => (ga.RatioX / ga.RatioY)))
			{
				gannAngle.Stroke.RenderTarget = RenderTarget;
				double	dx					= gannAngle.RatioX * chartControl.Properties.BarDistance;
				double	dVal				= gannAngle.RatioY * PointsPerBar;//NT7, just multiple directly this is price not pixels //chartScale.GetPixelsForDistance(gannAngle.RatioY * PointsPerBar);
				Vector	gannDataVector		= GetGannStepDataVector(dx, dVal);
				Point	extendedEndPoint	= CalculateExtendedDataPoint(panel, chartScale, Convert.ToInt32(anchorPoint.X), Anchor.Price, gannDataVector);

				if (!IsTextDisplayed || IsInHitTest)
					continue;

				SimpleFont						wpfFont		= chartControl.Properties.LabelFont ?? new SimpleFont();
				SharpDX.DirectWrite.TextFormat	textFormat	= wpfFont.ToDirectWriteTextFormat();
				textFormat.TextAlignment					= SharpDX.DirectWrite.TextAlignment.Leading;
				textFormat.WordWrapping						= SharpDX.DirectWrite.WordWrapping.NoWrap;
				SharpDX.DirectWrite.TextLayout	textLayout	= new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, gannAngle.Name, textFormat, 100, textFormat.FontSize);

				// once text is laid out, update used width to calcuated space required
				float fontHeight		= textLayout.Metrics.Height;

				Point textEndPoint		= new Point(extendedEndPoint.X, extendedEndPoint.Y);

				if (textEndPoint.X > panel.X + panel.W - textLayout.Metrics.Width)
				{
					textEndPoint.X = panel.X + panel.W - textLayout.Metrics.Width;
					textEndPoint.Y += textLayout.Metrics.Width;
				}

				if (gannDataVector.Y > 0)
				{
					if (textEndPoint.Y < panel.Y + (fontHeight * 0.5))
						textEndPoint.Y = panel.Y + (fontHeight * 0.5);
				}
				else
				{
					if (textEndPoint.Y > panel.Y + panel.H - (fontHeight * 1.5))
						textEndPoint.Y = panel.Y + panel.H - (fontHeight * 1.5);
				}

				float?	marginResource	= Application.Current.FindResource("FontModalTitleMargin") as float?;
				float	margin			= 2f + (marginResource.HasValue ? marginResource.Value : 3f);
				// Allow for changes in X position based on whether text is aligned to left or right edge of screen
				float	marginX			= FanDirection == GannFanDirection.DownLeft || FanDirection == GannFanDirection.UpLeft ? margin : -2 * margin;

				SharpDX.Vector2		endVec			= new SharpDX.Vector2((float) textEndPoint.X, (float) textEndPoint.Y);
				SharpDX.Matrix3x2	transformMatrix	= SharpDX.Matrix3x2.Translation(endVec);

				RenderTarget.Transform				= transformMatrix;

				RenderTarget.DrawTextLayout(new SharpDX.Vector2(marginX + margin, margin), textLayout, gannAngle.Stroke.BrushDX, SharpDX.Direct2D1.DrawTextOptions.NoSnap);

				RenderTarget.Transform				= SharpDX.Matrix3x2.Identity;
				textFormat.Dispose();
				textLayout.Dispose();
			}
		}
	}
	
	[TypeConverter("NinjaTrader.NinjaScript.DrawingTools.GannAngleTypeConverter")]
	public class GannAngle : NotifyPropertyChangedBase, IStrokeProvider, ICloneable
	{
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolsGannAngleRatioX", GroupName = "NinjaScriptGeneral")]
		public double RatioX { get; set; }
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolsGannAngleRatioY", GroupName = "NinjaScriptGeneral")]
		public double RatioY { get; set; }
		
		[Browsable(false)]
		public string Name
		{
			get { return string.Format("{0}x{1}", RatioX.ToString("0", Core.Globals.GeneralOptions.CurrentCulture), RatioY.ToString("0", Core.Globals.GeneralOptions.CurrentCulture)); }
		}
		
		public object AssemblyClone(Type t)
		{
			Assembly a 		= t.Assembly;
			object priceLevel 	= a.CreateInstance(t.FullName);
			
			foreach (PropertyInfo p in t.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
			{
				if (p.CanWrite)
				{
					if (p.PropertyType == typeof(Stroke))
					{
						Stroke copyStroke = new Stroke();
						Stroke.CopyTo(copyStroke);
						p.SetValue(priceLevel, copyStroke, null);
					}
					else
						p.SetValue(priceLevel, this.GetType().GetProperty(p.Name).GetValue(this), null);
				}
			}
			
			return priceLevel;
		}
		
		public virtual object Clone()
		{
			GannAngle newAngle = new GannAngle();
			CopyTo(newAngle);
			return newAngle;
		}

		public virtual void CopyTo(GannAngle other)
		{
			other.IsVisible = IsVisible;
			other.IsValueVisible = IsValueVisible;
			if (Stroke != null)
			{
				other.Stroke = new Stroke();
				Stroke.CopyTo(other.Stroke);
			}
			else 
				other.Stroke = null;
			other.Tag = Tag;
			other.RatioX = RatioX;
			other.RatioY = RatioY;
		}

		// parameterless constructor is needed for serialization and reflection in Clone
		public GannAngle() : this(1, 1, Brushes.Gray) { }

		public GannAngle(double ratioX, double ratioY, Brush strokeBrush)
		{
			IsValueVisible	= false;
			RatioX			= ratioX;
			RatioY			= ratioY;
			Stroke			= new Stroke(strokeBrush, 2f);
			IsVisible 		= true;
		}
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolsPriceLevelIsVisible", GroupName = "NinjaScriptGeneral")]
		public bool 	IsVisible 	{ get; set; }

		[XmlIgnore]
		[Browsable(false)]
		public bool		IsValueVisible	{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolsPriceLevelLineStroke", GroupName = "NinjaScriptGeneral")]
		public Stroke 	Stroke 		{ get; set; }

		[XmlIgnore]
		[Browsable(false)]
		public object Tag { get; set; }
	}
	
	public class GannAngleTypeConverter : TypeConverter
	{
		public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object component, Attribute[] attrs)
		{
			GannAngle gannAngle = component as GannAngle;
			PropertyDescriptorCollection propertyDescriptorCollection = base.GetPropertiesSupported(context) ?
				base.GetProperties(context, component, attrs) : TypeDescriptor.GetProperties(component, attrs);

			if (gannAngle == null || propertyDescriptorCollection == null)
				return null;

			PropertyDescriptorCollection filtered = new PropertyDescriptorCollection(null);
			foreach (PropertyDescriptor property in propertyDescriptorCollection)
			{
				if ((property.Name != "Value" || gannAngle.IsValueVisible) && property.IsBrowsable)
					filtered.Add(property);
			}

			return filtered;
		}

		public override bool GetPropertiesSupported(ITypeDescriptorContext context)
		{
			return true;
		}
	}
	
	public abstract class GannAngleContainer : DrawingTool
	{
		[PropertyEditor("NinjaTrader.Gui.Tools.CollectionEditor")]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolsGannAngles", Prompt = "NinjaScriptDrawingToolsGannAnglesPrompt", GroupName = "NinjaScriptGeneral", Order = 99)]
		[SkipOnCopyTo(true)]
		public List<GannAngle> GannAngles { get; set; }

		public override void CopyTo(NinjaScript ninjaScript)
		{
			base.CopyTo(ninjaScript);

			Type			newInstType				= ninjaScript.GetType();
			System.Reflection.PropertyInfo	gannAnglePropertyInfo	= newInstType.GetProperty("GannAngles");
			if (gannAnglePropertyInfo == null)
				return;

			IList newInstGannAngles = gannAnglePropertyInfo.GetValue(ninjaScript) as IList;
			if (newInstGannAngles == null)
				return;

			// Since new instance could be past set defaults, clear any existing
			newInstGannAngles.Clear();
			foreach (GannAngle oldGannAngle in GannAngles)
			{
				try
				{
					// Clone from the new assembly here to prevent losing existing GannAngles on compile
					object newInstance = oldGannAngle.AssemblyClone(Core.Globals.AssemblyRegistry.GetType(typeof(GannAngle).FullName));
					
					if (newInstance == null)
						continue;
					
					newInstGannAngles.Add(newInstance);
				}
				catch (ArgumentException)
				{
					// In compiled assembly case, Add call will fail for different assemblies so do normal clone instead
					object newInstance = oldGannAngle.Clone();
					
					if (newInstance == null)
						continue;
					
					// Make sure to update our stroke to a new instance so we dont try to use the old one
					IStrokeProvider strokeProvider = newInstance as IStrokeProvider;
					if (strokeProvider != null)
					{
						Stroke oldStroke = strokeProvider.Stroke;
						strokeProvider.Stroke = new Stroke();
						oldStroke.CopyTo(strokeProvider.Stroke);
					}
					
					newInstGannAngles.Add(newInstance);
				}
				catch { }
			}
		}

		protected GannAngleContainer()
		{
			GannAngles = new List<GannAngle>();
		}
	}
	
	public static partial class Draw
	{
		private static GannFan GannFanCore(NinjaScriptBase owner, bool isAutoScale, string tag, int barsAgo, DateTime time, double y, bool isGlobal, string templateName)
		{
			if (owner == null)
				throw new ArgumentException("owner");
			if (time == Core.Globals.MinDate && barsAgo == int.MinValue)
				throw new ArgumentException("bad start/end date/time");

			if (string.IsNullOrWhiteSpace(tag))
				throw new ArgumentException("tag cant be null or empty");

			if (isGlobal && tag[0] != '@')
				tag = "@" + tag;

			GannFan gannFan = DrawingTool.GetByTagOrNew(owner, typeof(GannFan), tag, templateName) as GannFan;
			if (gannFan == null)
				return null;

			DrawingTool.SetDrawingToolCommonValues(gannFan, tag, isAutoScale, owner, isGlobal);

			ChartAnchor anchor = DrawingTool.CreateChartAnchor(owner, barsAgo, time, y);
			anchor.CopyDataValues(gannFan.Anchor);

			gannFan.SetState(State.Active);
			return gannFan;
		}

		/// <summary>
		/// Draws a Gann Fan.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <returns></returns>
		public static GannFan GannFan(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y)
		{
			return GannFanCore(owner, isAutoScale, tag, barsAgo, Core.Globals.MinDate, y, false, null);
		}

		/// <summary>
		/// Draws a Gann Fan.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <returns></returns>
		public static GannFan GannFan(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime time, double y)
		{
			return GannFanCore(owner, isAutoScale, tag, int.MinValue, time, y, false, null);
		}

		/// <summary>
		/// Draws a Gann Fan.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="barsAgo">The bar the object will be drawn at. A value of 10 would be 10 bars ago</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static GannFan GannFan(NinjaScriptBase owner, string tag, bool isAutoScale, int barsAgo, double y, bool isGlobal, string templateName)
		{
			return GannFanCore(owner, isAutoScale, tag, barsAgo, Core.Globals.MinDate, y, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a Gann Fan.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="time"> The time the object will be drawn at.</param>
		/// <param name="y">The y value or Price for the object</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static GannFan GannFan(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime time, double y, bool isGlobal, string templateName)
		{
			return GannFanCore(owner, isAutoScale, tag, int.MinValue, time, y, isGlobal, templateName);
		}
	}
}
