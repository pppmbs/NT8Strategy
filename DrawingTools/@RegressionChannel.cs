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
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.DrawingTools
{
	// for disabling the StdDeviationUpperDistance and StdDeviationLowerDistance when Segment is chosen for SelectedChannelType
	public class RegressionChannelTypeConverter : Gui.DrawingTools.DrawingToolPropertiesConverter
	{
		// when using a custom type converter for a drawing tool, it must inherit from DrawingToolsPropertiesConverter
		// or else properties for chart anchors and the drawing tool will not be correctly reflected on the UI.

		public override bool GetPropertiesSupported(ITypeDescriptorContext context)
		{
			return true;
		}

		public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object value, Attribute[] attributes)
		{
			// override the GetProperties method to return a property collection that has these items removed and re-added with the IsReadOnly set to true.
			PropertyDescriptorCollection propertyDescriptorCollection = base.GetPropertiesSupported(context) ? base.GetProperties(context, value, attributes) : TypeDescriptor.GetProperties(value, attributes);

			RegressionChannel thisRegressionChannelInstance = (RegressionChannel)value;
			RegressionChannel.RegressionChannelType selectedChannelType = thisRegressionChannelInstance.ChannelType;
			if (selectedChannelType == RegressionChannel.RegressionChannelType.StandardDeviation)
				return propertyDescriptorCollection;

			PropertyDescriptorCollection adjusted = new PropertyDescriptorCollection(null);
			foreach (PropertyDescriptor thisDescriptor in propertyDescriptorCollection)
			{
				// as we loop through, if the item is the item we are looking for, we don't add the original but substitute a new property which sets it to read only
				if (thisDescriptor.Name == "StandardDeviationUpperDistance" || thisDescriptor.Name == "StandardDeviationLowerDistance")
					adjusted.Add(new PropertyDescriptorExtended(thisDescriptor, o => value, null, new Attribute[] { new ReadOnlyAttribute(true) }));
				// but if not the item we are looking for add the original to the return collection
				else
					adjusted.Add(thisDescriptor);
			}
			return adjusted;
		}
	}

	// note: when using a type converter attribute with dynamically loaded assemblies such as NinjaTrader custom,
	// you must pass typeconverter a string parameter. passing a type will fail to resolve
	/// <summary>
	/// Represents an interface that exposes information regarding a Regression Channel IDrawingTool.
	/// </summary>
	[TypeConverter("NinjaTrader.NinjaScript.DrawingTools.RegressionChannelTypeConverter")]
	public class RegressionChannel : DrawingTool
	{
		[TypeConverter("NinjaTrader.Custom.ResourceEnumConverter")]
		public enum RegressionChannelType
		{
			Segment,
			StandardDeviation
		}

		private ChartAnchor		editingAnchor;

		public override IEnumerable<ChartAnchor> Anchors { get { return new[] { StartAnchor, EndAnchor }; } }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolRegressionChannelType", GroupName = "NinjaScriptGeneral", Order = 2)]
		[RefreshProperties(RefreshProperties.All)]
		public RegressionChannelType ChannelType { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolRegressionChannelStandardDeviationExtendLeft", GroupName = "NinjaScriptLines")]
		public bool ExtendLeft { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolRegressionChannelStandardDeviationExtendRight", GroupName = "NinjaScriptLines")]
		public bool ExtendRight { get; set; }

		[Display(Order = 2)]
		public ChartAnchor EndAnchor	{ get; set; }

		public override object Icon { get { return Gui.Tools.Icons.DrawRegressionChannel; } }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolRegressionChannelLowerChannel", GroupName = "NinjaScriptLines", Order = 3)]
		public Stroke LowerChannelStroke { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolRegressionChannelRegressionChannel", GroupName = "NinjaScriptLines", Order = 2)]
		public Stroke RegressionStroke { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolRegressionChannelPriceType", GroupName = "NinjaScriptGeneral", Order = 1)]
		public PriceType PriceType { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolRegressionChannelStandardDeviationUpperDistance", GroupName = "NinjaScriptGeneral", Order = 3)]
		public double StandardDeviationUpperDistance { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolRegressionChannelStandardDeviationLowerDistance", GroupName = "NinjaScriptGeneral", Order = 4)]
		public double StandardDeviationLowerDistance { get; set; }

		[Display(Order = 1)]
		public ChartAnchor StartAnchor	{ get; set; }

		public override bool SupportsAlerts { get { return true; } }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolRegressionChannelUpperChannel", GroupName = "NinjaScriptLines", Order = 1)]
		public Stroke UpperChannelStroke { get; set; }
	
		// Override this to prevent copy + paste from changing anchors since anchors are always tied to bars series
		public override void AddPastedOffset(ChartPanel panel, ChartScale chartScale) { }
		
		// here we calculate the regression line, calculate the deviation, and then create 6 end points for each of the 3 lines
		// returns upper 2 points, middle 2 points, lower 2 points

		public double[] CalculateRegressionPriceValues(Bars baseBars, int startIndex, int endIndex)
		{
			double middleStartPrice, middleEndPrice, upperStartPrice, upperEndPrice, lowerStartPrice, lowerEndPrice = 0;

			if (startIndex == endIndex)
			{
				middleStartPrice = GetBarPrice(baseBars, endIndex);
				middleEndPrice = GetBarPrice(baseBars, endIndex);
				upperStartPrice = GetBarPrice(baseBars, endIndex);
				upperEndPrice = GetBarPrice(baseBars, endIndex);
				lowerStartPrice = GetBarPrice(baseBars, endIndex);
				lowerEndPrice = GetBarPrice(baseBars, endIndex);

				return new double[] {
				upperStartPrice,
				upperEndPrice,
				middleStartPrice,
				middleEndPrice,
				lowerStartPrice,
				lowerEndPrice
			};
			}

			// ignore actual positions and just count bars
			int beginIndex		=			 (startIndex < endIndex) ? startIndex : endIndex;

			int barsTotal					= Math.Abs(endIndex - startIndex) + 1;
			double sumX						= barsTotal * (barsTotal - 1) * 0.5;
			double divisor					= sumX * sumX - barsTotal * barsTotal * (barsTotal - 1d) * (2d * barsTotal - 1) / 6d;

			double sumXY					= 0;
			double sumY						= 0;

			for (int count = 0; count < barsTotal; count++)
			{
				int idx						= beginIndex + count;

				if (idx < baseBars.Count)
				{
					double priceValue = GetBarPrice(baseBars, idx);
					sumXY += count * priceValue;
					sumY += priceValue;
				}
			}

			double slope					= (barsTotal * sumXY - sumX * sumY) / divisor;
			double intercept				= (sumY - slope * sumX) / barsTotal;

			double sumResiduals				= 0;
			for (int count = 0; count < barsTotal; count++)
			{
				int idx = beginIndex + count;
				if (idx < baseBars.Count)
				{
					double regressionValue	= Math.Abs(GetBarPrice(baseBars, idx) - (intercept + slope * ((double)barsTotal - 1 - count)));
					sumResiduals += regressionValue;
				}
			}

			double avgResiduals	= sumResiduals / barsTotal;

			sumResiduals					= 0;
			for (int count = 0; count < barsTotal; count++)
			{
				int idx = beginIndex + count;
				if (idx < baseBars.Count)
				{
					double regressionValue	= Math.Abs(GetBarPrice(baseBars, idx) - (intercept + slope * ((double)barsTotal - 1 - count)));
					sumResiduals += (regressionValue - avgResiduals) * (regressionValue - avgResiduals);
				}
			}

			double stdDeviation				= Math.Sqrt(sumResiduals / barsTotal);

			middleStartPrice			= intercept + slope * ((double)barsTotal - 1);
			middleEndPrice			= intercept;
			upperStartPrice			= middleStartPrice + stdDeviation * StandardDeviationUpperDistance;
			upperEndPrice			= intercept + stdDeviation * StandardDeviationUpperDistance;
			lowerStartPrice			= middleStartPrice - stdDeviation * StandardDeviationLowerDistance;
			lowerEndPrice			= intercept - stdDeviation * StandardDeviationLowerDistance;

			// if the user pulled the end anchor to before the beginning anchor, reverse the points (and the deviation).
			if (startIndex > endIndex)
			{
				middleStartPrice			= intercept;
				middleEndPrice				= intercept - slope * (-1 * (double)barsTotal + 1);
				upperStartPrice				= intercept - stdDeviation * -StandardDeviationUpperDistance;
				upperEndPrice				= middleEndPrice - stdDeviation * -StandardDeviationUpperDistance;
				lowerStartPrice				= middleStartPrice + stdDeviation * -StandardDeviationLowerDistance;
				lowerEndPrice				= middleEndPrice + stdDeviation * -StandardDeviationLowerDistance;
			}

			// if segment, calculate using...
			if (ChannelType == RegressionChannelType.Segment)
			{
				int highIndexRev			= int.MinValue;
				int lowIndexRev				= int.MaxValue;
				double highValueRev			= double.MinValue;
				double lowValueRev			= double.MaxValue;
				for (int count = 0; count < barsTotal; count++)
				{
					int idx					= beginIndex + count;
					if (highValueRev < baseBars.GetHigh(idx))
					{
						highValueRev		= baseBars.GetHigh(idx);
						highIndexRev		= idx;
					}
					if (lowValueRev > baseBars.GetLow(idx))
					{
						lowValueRev			= baseBars.GetLow(idx);
						lowIndexRev			= idx;
					}
				}

				double upperDistance		= highValueRev - (intercept + slope * (endIndex - highIndexRev));
				double lowerDistance		= intercept + slope * (endIndex - lowIndexRev) - lowValueRev;

				upperStartPrice				= middleStartPrice + upperDistance;
				upperEndPrice				= middleEndPrice + upperDistance;
				lowerStartPrice				= middleStartPrice - lowerDistance;
				lowerEndPrice				= middleEndPrice - lowerDistance;

				// if the segment has the end point dragged before the start point
				if (startIndex > endIndex)
				{
					highIndexRev			= int.MinValue;
					lowIndexRev				= int.MaxValue;
					highValueRev			= double.MinValue;
					lowValueRev				= double.MaxValue;
					for (int count = 0; count < barsTotal; count++)
					{
						int idx				= endIndex + count;
						if (highValueRev < baseBars.GetHigh(idx))
						{
							highValueRev	= baseBars.GetHigh(idx);
							highIndexRev	= idx;
						}
						if (lowValueRev > baseBars.GetLow(idx))
						{
							lowValueRev		= baseBars.GetLow(idx);
							lowIndexRev		= idx;
						}
					}

					upperDistance			= highValueRev - (intercept + slope * (startIndex - highIndexRev));
					lowerDistance			= intercept + slope * (startIndex - lowIndexRev) - lowValueRev;

					upperStartPrice			= middleStartPrice + Math.Abs(upperDistance);
					upperEndPrice			= middleEndPrice + Math.Abs(upperDistance);
					lowerStartPrice			= middleStartPrice - Math.Abs(lowerDistance);
					lowerEndPrice			= middleEndPrice - Math.Abs(lowerDistance);
				}
			}

			return new double[] {
				upperStartPrice,
				upperEndPrice,
				middleStartPrice,
				middleEndPrice,
				lowerStartPrice,
				lowerEndPrice
			};
		}

		private Point[] CreateRegressionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			if (chartControl.BarsArray.Count == 0)
				return null;

			ChartPanel	chartPanel	= chartControl.ChartPanels[chartScale.PanelIndex];
			Point		startPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point		endPoint	= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);

			Bars baseBars = GetAttachedToChartBars().Bars;

			// note: dont use anchor slotIndex directly, it can be irrelevant on time based charts (or not yet set on EQ charts)
			int startIndex			= baseBars.GetBar(StartAnchor.Time);
			int endIndex			= baseBars.GetBar(EndAnchor.Time);

			double[] regressionPrices = CalculateRegressionPriceValues(baseBars, startIndex, endIndex);

			return new []
			{
				new Point(startPoint.X,	chartScale.GetYByValue(regressionPrices[0])),
				new Point(endPoint.X,	chartScale.GetYByValue(regressionPrices[1])),
				new Point(startPoint.X,	chartScale.GetYByValue(regressionPrices[2])),
				new Point(endPoint.X,	chartScale.GetYByValue(regressionPrices[3])),
				new Point(startPoint.X,	chartScale.GetYByValue(regressionPrices[4])),
				new Point(endPoint.X,	chartScale.GetYByValue(regressionPrices[5]))
			};
		}

		// this method returns the price based on the price type chosen
		public double GetBarPrice(Bars barObject, int barIndex)
		{
			if (barObject == null || !barObject.IsValidDataPointAt(barIndex))
				return double.MinValue;

			switch (PriceType)
			{
				case PriceType.High:		return barObject.GetHigh(barIndex);
				case PriceType.Low:			return barObject.GetLow(barIndex);
				case PriceType.Open:		return barObject.GetOpen(barIndex);
				case PriceType.Median:		return (barObject.GetHigh(barIndex) + barObject.GetLow(barIndex)) / 2;
				case PriceType.Typical:		return (barObject.GetHigh(barIndex) + barObject.GetLow(barIndex) + barObject.GetClose(barIndex)) / 3;
				case PriceType.Weighted:	return (barObject.GetHigh(barIndex) + barObject.GetLow(barIndex) + (barObject.GetClose(barIndex) * 2)) / 4;
				default:					return barObject.GetClose(barIndex);
			}
		}

		public override IEnumerable<AlertConditionItem> GetAlertConditionItems()
		{
			yield return new AlertConditionItem
			{
				ShouldOnlyDisplayName = true,
				Name = Custom.Resource.NinjaScriptDrawingToolRegressionChannelUpperChannel,
			};
			yield return new AlertConditionItem
			{
				ShouldOnlyDisplayName = true,
				Name = Custom.Resource.NinjaScriptDrawingToolRegressionChannel,
			};
			yield return new AlertConditionItem
			{
				ShouldOnlyDisplayName = true,
				Name = Custom.Resource.NinjaScriptDrawingToolRegressionChannelLowerChannel,
			};
		}

		public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:
					return Cursors.Pen;
				case DrawingState.Moving:
					return IsLocked ? Cursors.No : Cursors.SizeAll;
				case DrawingState.Editing:
					if (IsLocked)
						return Cursors.No;
					return editingAnchor == StartAnchor ? Cursors.SizeNESW : Cursors.SizeNWSE;
				default:
					ChartAnchor closestAnchor = GetClosestAnchor(chartControl, chartPanel, chartScale, 10, point);
					if (closestAnchor != null)
					{
						if (IsLocked)
							return Cursors.Arrow;
						return closestAnchor == StartAnchor ? Cursors.SizeNWSE : Cursors.SizeNESW;
					}

					Point endPoint			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
					Point startPoint		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);

					// take into account extended lines for selection!
					// dont overwrite existing, that can mess up getting extended point
					Point checkStartPoint	= startPoint;
					Point checkEndPoint		= endPoint; 
					
					if (ExtendLeft)
						checkStartPoint = GetExtendedPoint(chartControl, chartPanel, chartScale, EndAnchor, StartAnchor);
					if (ExtendRight)
						checkEndPoint = GetExtendedPoint(chartControl, chartPanel, chartScale, StartAnchor, EndAnchor);

					Vector regressionVector	= checkEndPoint - checkStartPoint;
					bool isAlongLine = MathHelper.IsPointAlongVector(point, checkStartPoint, regressionVector, 10);
					if (!isAlongLine)
						return null;
					return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
			}
		}

		public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel	chartPanel	= chartControl.ChartPanels[PanelIndex];
			Point		startPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point		endPoint	= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point		midPoint	= new Point((startPoint.X + endPoint.X) / 2, (startPoint.Y + endPoint.Y) / 2);
			return new[] { startPoint, midPoint, endPoint };
		}

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values, ChartControl chartControl, ChartScale chartScale)
		{
			Point[] regressionPoints = CreateRegressionPoints(chartControl, chartScale);
			if (regressionPoints == null)
				return false;

			Point startPoint;
			Point endPoint;
			// figure out whether we are checking top/middle/bottom line based on name of selected alert criteria
			if (conditionItem.Name == Custom.Resource.NinjaScriptDrawingToolRegressionChannelUpperChannel)
			{
				startPoint	= regressionPoints[0];
				endPoint	= regressionPoints[1];
			}
			else if (conditionItem.Name == Custom.Resource.NinjaScriptDrawingToolRegressionChannel)
			{
				// middle line
				startPoint	= regressionPoints[2];
				endPoint	= regressionPoints[3];
			}
			else
			{
				startPoint	= regressionPoints[4];
				endPoint	= regressionPoints[5];
			}

			// take into account extended left/right
			// dont overwrite while getting end point, can mess up getting extended point
			Point finalStartPoint	= startPoint;
			Point finalEndPoint		= endPoint;
			if (ExtendLeft)
				finalStartPoint	= GetExtendedPoint(chartControl, chartControl.ChartPanels[PanelIndex], chartScale, EndAnchor, StartAnchor);
			if (ExtendRight)
				finalEndPoint	= GetExtendedPoint(chartControl, chartControl.ChartPanels[PanelIndex], chartScale, StartAnchor, EndAnchor);

			// do not try to check Y because lines could cross through stuff
			double firstBarX = chartControl.GetXByTime(values[0].Time);
			double firstBarY = chartScale.GetYByValue(values[0].Value);
			
			double minLineX = double.MaxValue;
			double maxLineX = double.MinValue; 
			foreach (Point point in new[]{startPoint, endPoint})
			{
				minLineX = Math.Min(minLineX, point.X);
				maxLineX = Math.Max(maxLineX, point.X);
			}

			if (maxLineX < firstBarX) // bars passed our drawing tool
				return false;

			// NOTE: normalize line points so the leftmost is passed first. Otherwise, our vector
			// math could end up having the line normal vector being backwards if user drew it backwards.
			// but we dont care the order of anchors, we want 'up' to mean 'up'!
			Point leftPoint		= finalStartPoint.X < finalEndPoint.X ? startPoint : finalEndPoint;
			Point rightPoint	= finalEndPoint.X > finalStartPoint.X ? finalEndPoint : finalStartPoint;

			Point barPoint = new Point(firstBarX, firstBarY);

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
			Point[] regressionPoints = CreateRegressionPoints(chartControl, chartScale);
			if (regressionPoints == null)
				return false;

			Point		startPoint	= regressionPoints[2];
			Point		endPoint	= regressionPoints[3];

			// we dont want to update start point in place or else that can screw up getting extended end point,
			// if getting extended left ends up right where end point is, in that case same point is returned
			Point finalStartPoint = startPoint;
			Point finalEndPoint = endPoint;
			// take into account extended left/right
			if (ExtendLeft)
				finalStartPoint =  GetExtendedPoint(chartControl, chartControl.ChartPanels[PanelIndex], chartScale, EndAnchor, StartAnchor);
			if (ExtendRight)
				finalEndPoint =  GetExtendedPoint(chartControl, chartControl.ChartPanels[PanelIndex], chartScale, StartAnchor, EndAnchor);

			DateTime startTime	= chartControl.GetTimeByX((int) finalStartPoint.X);
			DateTime endTime	= chartControl.GetTimeByX((int) finalEndPoint.X);

			if (startTime >= firstTimeOnChart && startTime <= lastTimeOnChart)
				return true;
			if (endTime >= firstTimeOnChart && endTime <= lastTimeOnChart)
				return true;

			// crossthrough
			if ((endTime < firstTimeOnChart && startTime > lastTimeOnChart) ||
				(startTime < firstTimeOnChart && endTime > lastTimeOnChart))
				return true;
			return false;
		}

		public override void OnBarsChanged()
		{
			if (cControl != null && cScale != null)
				SetAnchorsToRegression(cControl, cScale);
		}

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;

			if (!this.IsVisible)
				return;

			MinValue = (StartAnchor.Price > EndAnchor.Price) ? StartAnchor.Price : EndAnchor.Price;
			MaxValue = (StartAnchor.Price > EndAnchor.Price) ? EndAnchor.Price : StartAnchor.Price;
		}

		public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:
					if (StartAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(StartAnchor);
						dataPoint.CopyDataValues(EndAnchor);
						StartAnchor.IsEditing = false;
					}
					else if (EndAnchor.IsEditing)
					{
						EndAnchor.IsEditing = false;
					}

					if (!StartAnchor.IsEditing && !EndAnchor.IsEditing)
					{
						DrawingState = DrawingState.Normal;
						IsSelected = false;
					}
					SetAnchorsToRegression(chartControl, chartScale);
					break;

				case DrawingState.Normal:
					Point point			= dataPoint.GetPoint(chartControl, chartPanel, chartScale);
					editingAnchor		= GetClosestAnchor(chartControl, chartPanel, chartScale, 10, point);
					if (editingAnchor != null)
					{
						editingAnchor.IsEditing = true;
						DrawingState = DrawingState.Editing;
					}
					else if (GetCursor(chartControl, chartPanel, chartScale, point) == null)
						// user whiffed
						IsSelected = false;
					else
						DrawingState = DrawingState.Moving;
					break;
			}
		}

		public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (IsLocked && DrawingState != DrawingState.Building)
				return;

			Bars		baseBars	= GetAttachedToChartBars().Bars;

			DateTime	barMinTime	= baseBars.GetTime(0);
			DateTime barMaxTime = DateTime.MinValue;
			if (baseBars.BarsPeriod.BarsPeriodType == BarsPeriodType.Day
				|| baseBars.BarsPeriod.BarsPeriodType == BarsPeriodType.Week
				|| baseBars.BarsPeriod.BarsPeriodType == BarsPeriodType.Month
				|| baseBars.BarsPeriod.BarsPeriodType == BarsPeriodType.Year)
				barMaxTime = baseBars.GetSessionEndTime(baseBars.Count - 1);
			else
				barMaxTime = baseBars.GetTime(baseBars.Count - 1);

			// use local variables for start and end anchor with the start anchor representing the lowest X value
			ChartAnchor startAnchor = (StartAnchor.Time < EndAnchor.Time) ? StartAnchor : EndAnchor;
			ChartAnchor endAnchor	= (StartAnchor.Time < EndAnchor.Time) ? EndAnchor : StartAnchor;
			DateTime desiredAnchorTime = dataPoint.Time;//chartControl.GetTimeByX((int) point.X);

			if (DrawingState == DrawingState.Building)
			{
				ChartAnchor curEditAnchor = Anchors.FirstOrDefault(a => a.IsEditing);
				if (curEditAnchor != null)
				{
					// when building, do not move the end anchor past bars
					if (desiredAnchorTime >= barMinTime && desiredAnchorTime <= barMaxTime)
					{
						dataPoint.CopyDataValues(curEditAnchor);
						SetAnchorsToRegression(chartControl, chartScale);
					}
				}
			}
			else if (DrawingState == DrawingState.Editing && editingAnchor != null)
			{
				// when editing, do not move the end anchor past bars
				if (desiredAnchorTime >= barMinTime && desiredAnchorTime <= barMaxTime)
				{
					dataPoint.CopyDataValues(editingAnchor);
					SetAnchorsToRegression(chartControl, chartScale);
				}
			}
			else if (DrawingState == DrawingState.Moving)
			{
				// make sure the rightmost anchor isnt going to go past last bar, 
				// and the leftmost anchor doesnt try to go past first bar.
				// do this by making two temporary anchors, moving them and only commiting move
				// if it doesnt go past limits
				ChartAnchor startAnchorMoved = new ChartAnchor();
				startAnchor.CopyTo(startAnchorMoved);
				ChartAnchor endAnchorMoved = new ChartAnchor();
				endAnchor.CopyTo(endAnchorMoved);

				startAnchorMoved.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
				endAnchorMoved.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
			
				if (startAnchorMoved.Time >= barMinTime && endAnchorMoved.Time <= barMaxTime)
				{
					// ok, we are still in bar limits so move our live anchors
					foreach (ChartAnchor anchor in Anchors)
						anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
					SetAnchorsToRegression(chartControl, chartScale);
				}
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

		private ChartControl cControl;
		private ChartScale cScale;
		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			// save chartControl and chartScale for use in OnBarsChanged
			cControl = chartControl;
			cScale = chartScale;
			// get a list of new y positions for each line in the channel
			Point[] regressionPoints = CreateRegressionPoints(chartControl, chartScale);
			if (regressionPoints == null)
				return;

			RegressionStroke.RenderTarget	= RenderTarget;
			UpperChannelStroke.RenderTarget	= RenderTarget;
			LowerChannelStroke.RenderTarget	= RenderTarget;

			// apply antialias mode to smooth the lines
			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

			// if extended, get extended points
			// (But do this after the anchors are updated so we don't move the anchors)
			// note, dont overwrite points in place, that will mess up the rolling extension calcuation
			Point[] renderPoints = regressionPoints.ToArray();
			for (int i = 0; i < regressionPoints.Length; i += 2)
			{
				if (ExtendLeft)
					renderPoints[i] = GetExtendedPoint(regressionPoints[i + 1], regressionPoints[i]);

				if (ExtendRight)
					renderPoints[i + 1] = GetExtendedPoint(regressionPoints[i], regressionPoints[i + 1]);
			}

			// create the DX vectors for render with the calculated points
			SharpDX.Vector2 upperStartVec	= renderPoints[0].ToVector2();
			SharpDX.Vector2 upperEndVec		= renderPoints[1].ToVector2();
			SharpDX.Vector2 middleStartVec	= renderPoints[2].ToVector2();
			SharpDX.Vector2 middleEndVec	= renderPoints[3].ToVector2();
			SharpDX.Vector2 lowerStartVec	= renderPoints[4].ToVector2();
			SharpDX.Vector2 lowerEndVec		= renderPoints[5].ToVector2();

			// use the render objects to render the lines
			SharpDX.Direct2D1.Brush tmpBrush = IsInHitTest ? chartControl.SelectionBrush : UpperChannelStroke.BrushDX;
			RegressionStroke.RenderTarget.DrawLine(upperStartVec, upperEndVec, tmpBrush, UpperChannelStroke.Width, UpperChannelStroke.StrokeStyle);
			tmpBrush = IsInHitTest ? chartControl.SelectionBrush : RegressionStroke.BrushDX;
			RegressionStroke.RenderTarget.DrawLine(middleStartVec, middleEndVec, tmpBrush, RegressionStroke.Width, RegressionStroke.StrokeStyle);
			tmpBrush = IsInHitTest ? chartControl.SelectionBrush : LowerChannelStroke.BrushDX;
			LowerChannelStroke.RenderTarget.DrawLine(lowerStartVec, lowerEndVec, tmpBrush, LowerChannelStroke.Width, LowerChannelStroke.StrokeStyle);
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				UpperChannelStroke		= new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Solid, 2f);
				RegressionStroke		= new Stroke(Brushes.DarkGray, DashStyleHelper.Solid, 2f);
				LowerChannelStroke		= new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Solid, 2f);
				Description				= Custom.Resource.NinjaScriptDrawingToolRegressionChannel;
				Name					= Custom.Resource.NinjaScriptDrawingToolRegressionChannel;

				ChannelType				= RegressionChannelType.StandardDeviation;
				PriceType				= PriceType.Close;

				StandardDeviationUpperDistance = 2;
				StandardDeviationLowerDistance = 2;

				StartAnchor = new ChartAnchor
				{
					IsEditing	= true,
					DrawingTool	= this,
					DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchorStart,
				};
				EndAnchor = new ChartAnchor
				{
					IsEditing	= true,
					DrawingTool	= this,
					DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchorEnd,
				};
			}
			else if (State == State.Terminated)
				Dispose();
		}

		private void SetAnchorsToRegression(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel chartPanel		= chartControl.ChartPanels[chartScale.PanelIndex];
			Point[] regressionPoints	= CreateRegressionPoints(chartControl, chartScale);

			StartAnchor.UpdateYFromDevicePoint(regressionPoints[2], chartScale);
			EndAnchor.UpdateYFromDevicePoint(regressionPoints[3], chartScale);
		}
	}

	public static partial class Draw
	{
		private static RegressionChannel RegressionChannelCore(NinjaScriptBase owner, string tag,
			bool isAutoScale, int startBarsAgo, DateTime startTime, int endBarsAgo, DateTime endTime,
			Brush upperBrush, DashStyleHelper upperDashStyle, float? upperWidth,
			Brush middleBrush, DashStyleHelper middleDashStyle, float? middleWidth,
			Brush lowerBrush, DashStyleHelper lowerDashStyle, float? lowerWidth, bool isGlobal, string templateName)
		{
			if (owner == null)
				throw new ArgumentException("owner");

			if (string.IsNullOrWhiteSpace(tag))
				throw new ArgumentException("tag cant be null or empty", "tag");

			if (isGlobal && tag[0] != GlobalDrawingToolManager.GlobalDrawingToolTagPrefix)
				tag = GlobalDrawingToolManager.GlobalDrawingToolTagPrefix + tag;

			RegressionChannel regChannel = DrawingTool.GetByTagOrNew(owner, typeof(RegressionChannel), tag, templateName) as RegressionChannel;
			
			if (regChannel == null)
				return null;

			DrawingTool.SetDrawingToolCommonValues(regChannel, tag, isAutoScale, owner, isGlobal);

			int			currentBar		= DrawingTool.GetCurrentBar(owner);
			double[]	regressionPoints;
			if (startBarsAgo > int.MinValue && endBarsAgo > int.MinValue)
				regressionPoints = regChannel.CalculateRegressionPriceValues(owner.BarsArray[0], currentBar - startBarsAgo, currentBar - endBarsAgo);
			else if (startTime > Core.Globals.MinDate && endTime > Core.Globals.MinDate)
				regressionPoints = regChannel.CalculateRegressionPriceValues(owner.BarsArray[0], owner.BarsArray[0].GetBar(startTime), owner.BarsArray[0].GetBar(endTime));
			else
				throw new ArgumentException("Bad start / end values");

			ChartAnchor	startAnchor	= DrawingTool.CreateChartAnchor(owner, startBarsAgo, startTime, regressionPoints[2]);
			ChartAnchor	endAnchor	= DrawingTool.CreateChartAnchor(owner, endBarsAgo, endTime, regressionPoints[3]);

			// regression channel calculates y values on anchors based on bar range, so just pass in 0 for price values
			startAnchor.SlotIndex = endAnchor.SlotIndex = double.MinValue;
			startAnchor.CopyDataValues(regChannel.StartAnchor);
			endAnchor.CopyDataValues(regChannel.EndAnchor);

			// stroke / dash stuff is very flexible so rip through all of it
			// if just upper color is present (which it should always be), set all three strokes to that color 
			// like NT7. however if a templateName is being used, skip all brush handling
			if (string.IsNullOrEmpty(templateName))
			{
				Brush middleBrush2Use	= middleBrush ?? upperBrush;
				Brush lowerBrush2Use	= lowerBrush ?? upperBrush;

				Stroke upperStroke = new Stroke(upperBrush);
				upperStroke.DashStyleHelper = upperDashStyle;
				if (upperWidth != null)
					upperStroke.Width = upperWidth.Value;

				Stroke midStroke = new Stroke(middleBrush2Use);
				midStroke.DashStyleHelper = middleDashStyle;
				if (middleWidth != null)
					midStroke.Width = middleWidth.Value;

				Stroke lowerStroke = new Stroke(lowerBrush2Use);
				lowerStroke.DashStyleHelper = lowerDashStyle;
				if (lowerWidth != null)
					lowerStroke.Width = lowerWidth.Value;

				upperStroke.CopyTo(regChannel.UpperChannelStroke);
				midStroke.CopyTo(regChannel.RegressionStroke);
				lowerStroke.CopyTo(regChannel.LowerChannelStroke);
			}

			regChannel.SetState(State.Active);
			return regChannel;
		}

		/// <summary>
		/// Draws a regression channel.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static RegressionChannel RegressionChannel(NinjaScriptBase owner, string tag, int startBarsAgo, int endBarsAgo, Brush brush)
		{
			return RegressionChannelCore(owner, tag, false, startBarsAgo, Core.Globals.MinDate, endBarsAgo, Core.Globals.MinDate,
				brush, DashStyleHelper.Solid, null, null, DashStyleHelper.Solid, null, null, DashStyleHelper.Solid, null, false, null);
		}

		/// <summary>
		/// Draws a regression channel.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="brush">The brush used to color draw object</param>
		/// <returns></returns>
		public static RegressionChannel RegressionChannel(NinjaScriptBase owner, string tag, DateTime startTime, DateTime endTime, Brush brush)
		{
			return RegressionChannelCore(owner, tag, false, int.MinValue, startTime, int.MinValue, endTime,
				brush, DashStyleHelper.Solid, null, null, DashStyleHelper.Solid, null, null, DashStyleHelper.Solid, null, false, null);
		}

		/// <summary>
		/// Draws a regression channel.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="upperBrush">The brush for the upper line</param>
		/// <param name="upperDashStyle">The DashStyle for the upper line</param>
		/// <param name="upperWidth">Width of the upper line.</param>
		/// <param name="middleBrush">The brush for the middle line</param>
		/// <param name="middleDashStyle">The DashStyle for the middle line</param>
		/// <param name="middleWidth">Width of the middle line.</param>
		/// <param name="lowerBrush">The brush for the lower line</param>
		/// <param name="lowerDashStyle">The DashStyle for the lower line</param>
		/// <param name="lowerWidth">Width of the lower line.</param>
		/// <returns></returns>
		public static RegressionChannel RegressionChannel(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, int endBarsAgo, 
			Brush upperBrush, DashStyleHelper upperDashStyle, int upperWidth, 
			Brush middleBrush, DashStyleHelper middleDashStyle, int middleWidth, 
			Brush lowerBrush, DashStyleHelper lowerDashStyle, int lowerWidth)
		{
			return RegressionChannelCore(owner, tag, isAutoScale, 
				startBarsAgo, Core.Globals.MinDate, 
				endBarsAgo, Core.Globals.MinDate,
				upperBrush, upperDashStyle, upperWidth,
				middleBrush, middleDashStyle, middleWidth,
				lowerBrush, lowerDashStyle, lowerWidth, false, null);
		}

		/// <summary>
		/// Draws a regression channel.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="isAutoScale">Determines if the draw object will be included in the y-axis scale</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="upperBrush">The brush for the upper line</param>
		/// <param name="upperDashStyle">The DashStyle for the upper line</param>
		/// <param name="upperWidth">Width of the upper line.</param>
		/// <param name="middleBrush">The brush for the middle line</param>
		/// <param name="middleDashStyle">The DashStyle for the middle line</param>
		/// <param name="middleWidth">Width of the middle line.</param>
		/// <param name="lowerBrush">The brush for the lower line</param>
		/// <param name="lowerDashStyle">The DashStyle for the lower line</param>
		/// <param name="lowerWidth">Width of the lower line.</param>
		/// <returns></returns>
		public static RegressionChannel RegressionChannel(NinjaScriptBase owner, string tag, bool isAutoScale, 
			DateTime startTime, DateTime endTime, 
			Brush upperBrush, DashStyleHelper upperDashStyle, int upperWidth, 
			Brush middleBrush, DashStyleHelper middleDashStyle, int middleWidth, 
			Brush lowerBrush, DashStyleHelper lowerDashStyle, int lowerWidth)
		{
			return RegressionChannelCore(owner, tag, isAutoScale, 
				int.MinValue, startTime, 
				int.MinValue, endTime,
				upperBrush, upperDashStyle, upperWidth,
				middleBrush, middleDashStyle, middleWidth,
				lowerBrush, lowerDashStyle, lowerWidth, false, null);
		}

		/// <summary>
		/// Draws a regression channel.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startBarsAgo">The starting bar (x axis coordinate) where the draw object will be drawn. For example, a value of 10 would paint the draw object 10 bars back.</param>
		/// <param name="endBarsAgo">The end bar (x axis coordinate) where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static RegressionChannel RegressionChannel(NinjaScriptBase owner, string tag, int startBarsAgo, int endBarsAgo, bool isGlobal, string templateName)
		{
			return RegressionChannelCore(owner, tag, false, startBarsAgo, Core.Globals.MinDate, endBarsAgo, Core.Globals.MinDate,
				null, DashStyleHelper.Solid, null, null, DashStyleHelper.Solid, null, null, DashStyleHelper.Solid, null, isGlobal, templateName);
		}

		/// <summary>
		/// Draws a regression channel.
		/// </summary>
		/// <param name="owner">The hosting NinjaScript object which is calling the draw method</param>
		/// <param name="tag">A user defined unique id used to reference the draw object</param>
		/// <param name="startTime">The starting time where the draw object will be drawn.</param>
		/// <param name="endTime">The end time where the draw object will terminate</param>
		/// <param name="isGlobal">Determines if the draw object will be global across all charts which match the instrument</param>
		/// <param name="templateName">The name of the drawing tool template the object will use to determine various visual properties</param>
		/// <returns></returns>
		public static RegressionChannel RegressionChannel(NinjaScriptBase owner, string tag, DateTime startTime, DateTime endTime, bool isGlobal, string templateName)
		{
			return RegressionChannelCore(owner, tag, false, int.MinValue, startTime, int.MinValue, endTime,
				null, DashStyleHelper.Solid, null, null, DashStyleHelper.Solid, null, null, DashStyleHelper.Solid, null, isGlobal, templateName);
		}
	}
}