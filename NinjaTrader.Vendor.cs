// 
// Copyright (C) 2012, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Windows.Forms;
using System.Windows.Media;
using System.Xml.Serialization;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private WoodiesCCI[] cacheWoodiesCCI;
		public WoodiesCCI WoodiesCCI(int chopIndicatorWidth, int neutralBars, int period, int periodEma, int periodLinReg, int periodTurbo, int sideWinderLimit0, int sideWinderLimit1, int sideWinderWidth)
		{
			return WoodiesCCI(Input, chopIndicatorWidth, neutralBars, period, periodEma, periodLinReg, periodTurbo, sideWinderLimit0, sideWinderLimit1, sideWinderWidth);
		}

		public WoodiesCCI WoodiesCCI(ISeries<double> input, int chopIndicatorWidth, int neutralBars, int period, int periodEma, int periodLinReg, int periodTurbo, int sideWinderLimit0, int sideWinderLimit1, int sideWinderWidth)
		{
			if (cacheWoodiesCCI != null)
				for (int idx = 0; idx < cacheWoodiesCCI.Length; idx++)
					if (cacheWoodiesCCI[idx] != null && cacheWoodiesCCI[idx].ChopIndicatorWidth == chopIndicatorWidth && cacheWoodiesCCI[idx].NeutralBars == neutralBars && cacheWoodiesCCI[idx].Period == period && cacheWoodiesCCI[idx].PeriodEma == periodEma && cacheWoodiesCCI[idx].PeriodLinReg == periodLinReg && cacheWoodiesCCI[idx].PeriodTurbo == periodTurbo && cacheWoodiesCCI[idx].SideWinderLimit0 == sideWinderLimit0 && cacheWoodiesCCI[idx].SideWinderLimit1 == sideWinderLimit1 && cacheWoodiesCCI[idx].SideWinderWidth == sideWinderWidth && cacheWoodiesCCI[idx].EqualsInput(input))
						return cacheWoodiesCCI[idx];
			return CacheIndicator<WoodiesCCI>(new WoodiesCCI(){ ChopIndicatorWidth = chopIndicatorWidth, NeutralBars = neutralBars, Period = period, PeriodEma = periodEma, PeriodLinReg = periodLinReg, PeriodTurbo = periodTurbo, SideWinderLimit0 = sideWinderLimit0, SideWinderLimit1 = sideWinderLimit1, SideWinderWidth = sideWinderWidth }, input, ref cacheWoodiesCCI);
		}
	}

	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private WoodiesPivots[] cacheWoodiesPivots;
		public WoodiesPivots WoodiesPivots(NinjaTrader.NinjaScript.Indicators.HLCCalculationModeWoodie priorDayHlc, int width)
		{
			return WoodiesPivots(Input, priorDayHlc, width);
		}

		public WoodiesPivots WoodiesPivots(ISeries<double> input, NinjaTrader.NinjaScript.Indicators.HLCCalculationModeWoodie priorDayHlc, int width)
		{
			if (cacheWoodiesPivots != null)
				for (int idx = 0; idx < cacheWoodiesPivots.Length; idx++)
					if (cacheWoodiesPivots[idx] != null && cacheWoodiesPivots[idx].PriorDayHlc == priorDayHlc && cacheWoodiesPivots[idx].Width == width && cacheWoodiesPivots[idx].EqualsInput(input))
						return cacheWoodiesPivots[idx];
			return CacheIndicator<WoodiesPivots>(new WoodiesPivots(){ PriorDayHlc = priorDayHlc, Width = width }, input, ref cacheWoodiesPivots);
		}
	}
	
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private WisemanAlligator[] cacheWisemanAlligator;
		public WisemanAlligator WisemanAlligator(int jawPeriod, int teethPeriod, int lipsPeriod, int jawOffset, int teethOffset, int lipsOffset)
		{
			return WisemanAlligator(Input, jawPeriod, teethPeriod, lipsPeriod, jawOffset, teethOffset, lipsOffset);
		}

		public WisemanAlligator WisemanAlligator(ISeries<double> input, int jawPeriod, int teethPeriod, int lipsPeriod, int jawOffset, int teethOffset, int lipsOffset)
		{
			if (cacheWisemanAlligator != null)
				for (int idx = 0; idx < cacheWisemanAlligator.Length; idx++)
					if (cacheWisemanAlligator[idx] != null && cacheWisemanAlligator[idx].JawPeriod == jawPeriod && cacheWisemanAlligator[idx].TeethPeriod == teethPeriod && cacheWisemanAlligator[idx].LipsPeriod == lipsPeriod && cacheWisemanAlligator[idx].JawOffset == jawOffset && cacheWisemanAlligator[idx].TeethOffset == teethOffset && cacheWisemanAlligator[idx].LipsOffset == lipsOffset && cacheWisemanAlligator[idx].EqualsInput(input))
						return cacheWisemanAlligator[idx];
			return CacheIndicator<WisemanAlligator>(new WisemanAlligator(){ JawPeriod = jawPeriod, TeethPeriod = teethPeriod, LipsPeriod = lipsPeriod, JawOffset = jawOffset, TeethOffset = teethOffset, LipsOffset = lipsOffset }, input, ref cacheWisemanAlligator);
		}
	}
	
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private WisemanAwesomeOscillator[] cacheWisemanAwesomeOscillator;
		public WisemanAwesomeOscillator WisemanAwesomeOscillator()
		{
			return WisemanAwesomeOscillator(Input);
		}

		public WisemanAwesomeOscillator WisemanAwesomeOscillator(ISeries<double> input)
		{
			if (cacheWisemanAwesomeOscillator != null)
				for (int idx = 0; idx < cacheWisemanAwesomeOscillator.Length; idx++)
					if (cacheWisemanAwesomeOscillator[idx] != null &&  cacheWisemanAwesomeOscillator[idx].EqualsInput(input))
						return cacheWisemanAwesomeOscillator[idx];
			return CacheIndicator<WisemanAwesomeOscillator>(new WisemanAwesomeOscillator(), input, ref cacheWisemanAwesomeOscillator);
		}
	}
	
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private WisemanFractal[] cacheWisemanFractal;
		public WisemanFractal WisemanFractal(int strength, int triangleOffset)
		{
			return WisemanFractal(Input, strength, triangleOffset);
		}

		public WisemanFractal WisemanFractal(ISeries<double> input, int strength, int triangleOffset)
		{
			if (cacheWisemanFractal != null)
				for (int idx = 0; idx < cacheWisemanFractal.Length; idx++)
					if (cacheWisemanFractal[idx] != null && cacheWisemanFractal[idx].Strength == strength && cacheWisemanFractal[idx].TriangleOffset == triangleOffset && cacheWisemanFractal[idx].EqualsInput(input))
						return cacheWisemanFractal[idx];
			return CacheIndicator<WisemanFractal>(new WisemanFractal(){ Strength = strength, TriangleOffset = triangleOffset }, input, ref cacheWisemanFractal);
		}
	}
	
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private OrderFlowCumulativeDelta[] cacheOrderFlowCumulativeDelta;
		public OrderFlowCumulativeDelta OrderFlowCumulativeDelta(NinjaTrader.NinjaScript.Indicators.CumulativeDeltaType deltaType, CumulativeDeltaPeriod period, int sizeFilter)
		{
			return OrderFlowCumulativeDelta(Input, deltaType, period, sizeFilter);
		}

		public OrderFlowCumulativeDelta OrderFlowCumulativeDelta(ISeries<double> input, NinjaTrader.NinjaScript.Indicators.CumulativeDeltaType deltaType, CumulativeDeltaPeriod period, int sizeFilter)
		{
			if (cacheOrderFlowCumulativeDelta != null)
				for (int idx = 0; idx < cacheOrderFlowCumulativeDelta.Length; idx++)
					if (cacheOrderFlowCumulativeDelta[idx] != null && cacheOrderFlowCumulativeDelta[idx].DeltaType == deltaType && cacheOrderFlowCumulativeDelta[idx].Period == period && cacheOrderFlowCumulativeDelta[idx].SizeFilter == sizeFilter && cacheOrderFlowCumulativeDelta[idx].EqualsInput(input))
						return cacheOrderFlowCumulativeDelta[idx];
			return CacheIndicator<OrderFlowCumulativeDelta>(new OrderFlowCumulativeDelta() { DeltaType = deltaType, Period = period, SizeFilter = sizeFilter }, input, ref cacheOrderFlowCumulativeDelta);
		}
	}
	
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private OrderFlowMarketDepthMap[] cacheOrderFlowMarketDepthMap;
		public OrderFlowMarketDepthMap OrderFlowMarketDepthMap(BaseVolumeRange baseRange, int maxRange, int minRange, OpacityDistribution opacityDistribution, int depthMargin, bool extendLastKnown, bool showBidAskLine)
		{
			return OrderFlowMarketDepthMap(Input, baseRange, maxRange, minRange, opacityDistribution, depthMargin, extendLastKnown, showBidAskLine);
		}

		public OrderFlowMarketDepthMap OrderFlowMarketDepthMap(ISeries<double> input, BaseVolumeRange baseRange, int maxRange, int minRange, OpacityDistribution opacityDistribution, int depthMargin, bool extendLastKnown, bool showBidAskLine)
		{
			if (cacheOrderFlowMarketDepthMap != null)
				for (int idx = 0; idx < cacheOrderFlowMarketDepthMap.Length; idx++)
					if (cacheOrderFlowMarketDepthMap[idx] != null && cacheOrderFlowMarketDepthMap[idx].BaseRange == baseRange && cacheOrderFlowMarketDepthMap[idx].MaxRange == maxRange && cacheOrderFlowMarketDepthMap[idx].MinRange == minRange && cacheOrderFlowMarketDepthMap[idx].OpacityDistribution == opacityDistribution && cacheOrderFlowMarketDepthMap[idx].DepthMargin == depthMargin && cacheOrderFlowMarketDepthMap[idx].ExtendLastKnown == extendLastKnown && cacheOrderFlowMarketDepthMap[idx].ShowBidAskLine == showBidAskLine && cacheOrderFlowMarketDepthMap[idx].EqualsInput(input))
						return cacheOrderFlowMarketDepthMap[idx];
			return CacheIndicator<OrderFlowMarketDepthMap>(new OrderFlowMarketDepthMap() { BaseRange = baseRange, MaxRange = maxRange, MinRange = minRange, OpacityDistribution = opacityDistribution, DepthMargin = depthMargin, ExtendLastKnown = extendLastKnown, ShowBidAskLine = showBidAskLine }, input, ref cacheOrderFlowMarketDepthMap);
		}
	}
	
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private OrderFlowVWAP[] cacheOrderFlowVWAP;
		public OrderFlowVWAP OrderFlowVWAP(VWAPResolution resolution, Data.TradingHours tradingHoursInstance, VWAPStandardDeviations numStandardDeviations, double sD1Multiplier, double sD2Multiplier, double sD3Multiplier)
		{
			return OrderFlowVWAP(Input, resolution, tradingHoursInstance, numStandardDeviations, sD1Multiplier, sD2Multiplier, sD3Multiplier);
		}

		public OrderFlowVWAP OrderFlowVWAP(ISeries<double> input, VWAPResolution resolution, Data.TradingHours tradingHoursInstance, VWAPStandardDeviations numStandardDeviations, double sD1Multiplier, double sD2Multiplier, double sD3Multiplier)
		{
			if (cacheOrderFlowVWAP != null)
				for (int idx = 0; idx < cacheOrderFlowVWAP.Length; idx++)
					if (cacheOrderFlowVWAP[idx] != null && cacheOrderFlowVWAP[idx].Resolution == resolution && cacheOrderFlowVWAP[idx].TradingHoursInstance == tradingHoursInstance && cacheOrderFlowVWAP[idx].NumStandardDeviations == numStandardDeviations && cacheOrderFlowVWAP[idx].SD1Multiplier == sD1Multiplier && cacheOrderFlowVWAP[idx].SD2Multiplier == sD2Multiplier && cacheOrderFlowVWAP[idx].SD3Multiplier == sD3Multiplier && cacheOrderFlowVWAP[idx].EqualsInput(input))
						return cacheOrderFlowVWAP[idx];
			return CacheIndicator<OrderFlowVWAP>(new OrderFlowVWAP() { Resolution = resolution, TradingHoursInstance = tradingHoursInstance, NumStandardDeviations = numStandardDeviations, SD1Multiplier = sD1Multiplier, SD2Multiplier = sD2Multiplier, SD3Multiplier = sD3Multiplier }, input, ref cacheOrderFlowVWAP);
		}
	}
	
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private OrderFlowTradeDetector[] cacheOrderFlowTradeDetector;
		public OrderFlowTradeDetector OrderFlowTradeDetector(TradeDetectorBaseLargeVolumeOn baseLargeVolumeOn, int minimumVolumeForMarker, int maximumMarkerSize, TradeDetectorSizeBase baseMarkerSizeOn, bool hoverValues)
		{
			return OrderFlowTradeDetector(Input, baseLargeVolumeOn, minimumVolumeForMarker, maximumMarkerSize, baseMarkerSizeOn, hoverValues);
		}

		public OrderFlowTradeDetector OrderFlowTradeDetector(ISeries<double> input, TradeDetectorBaseLargeVolumeOn baseLargeVolumeOn, int minimumVolumeForMarker, int maximumMarkerSize, TradeDetectorSizeBase baseMarkerSizeOn, bool hoverValues)
		{
			if (cacheOrderFlowTradeDetector != null)
				for (int idx = 0; idx < cacheOrderFlowTradeDetector.Length; idx++)
					if (cacheOrderFlowTradeDetector[idx] != null && cacheOrderFlowTradeDetector[idx].BaseLargeVolumeOn == baseLargeVolumeOn && cacheOrderFlowTradeDetector[idx].MinimumVolumeForMarker == minimumVolumeForMarker && cacheOrderFlowTradeDetector[idx].MaximumMarkerSize == maximumMarkerSize && cacheOrderFlowTradeDetector[idx].BaseMarkerSizeOn == baseMarkerSizeOn && cacheOrderFlowTradeDetector[idx].HoverValues == hoverValues && cacheOrderFlowTradeDetector[idx].EqualsInput(input))
						return cacheOrderFlowTradeDetector[idx];
			return CacheIndicator<OrderFlowTradeDetector>(new OrderFlowTradeDetector() { BaseLargeVolumeOn = baseLargeVolumeOn, MinimumVolumeForMarker = minimumVolumeForMarker, MaximumMarkerSize = maximumMarkerSize, BaseMarkerSizeOn = baseMarkerSizeOn, HoverValues = hoverValues }, input, ref cacheOrderFlowTradeDetector);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.WoodiesCCI WoodiesCCI(int chopIndicatorWidth, int neutralBars, int period, int periodEma, int periodLinReg, int periodTurbo, int sideWinderLimit0, int sideWinderLimit1, int sideWinderWidth)
		{
			return indicator.WoodiesCCI(Input, chopIndicatorWidth, neutralBars, period, periodEma, periodLinReg, periodTurbo, sideWinderLimit0, sideWinderLimit1, sideWinderWidth);
		}

		public Indicators.WoodiesCCI WoodiesCCI(ISeries<double> input , int chopIndicatorWidth, int neutralBars, int period, int periodEma, int periodLinReg, int periodTurbo, int sideWinderLimit0, int sideWinderLimit1, int sideWinderWidth)
		{
			return indicator.WoodiesCCI(input, chopIndicatorWidth, neutralBars, period, periodEma, periodLinReg, periodTurbo, sideWinderLimit0, sideWinderLimit1, sideWinderWidth);
		}
	}

	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.WoodiesPivots WoodiesPivots(NinjaTrader.NinjaScript.Indicators.HLCCalculationModeWoodie priorDayHlc, int width)
		{
			return indicator.WoodiesPivots(Input, priorDayHlc, width);
		}

		public Indicators.WoodiesPivots WoodiesPivots(ISeries<double> input , NinjaTrader.NinjaScript.Indicators.HLCCalculationModeWoodie priorDayHlc, int width)
		{
			return indicator.WoodiesPivots(input, priorDayHlc, width);
		}
	}
	
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.WisemanAlligator WisemanAlligator(int jawPeriod, int teethPeriod, int lipsPeriod, int jawOffset, int teethOffset, int lipsOffset)
		{
			return indicator.WisemanAlligator(Input, jawPeriod, teethPeriod, lipsPeriod, jawOffset, teethOffset, lipsOffset);
		}

		public Indicators.WisemanAlligator WisemanAlligator(ISeries<double> input , int jawPeriod, int teethPeriod, int lipsPeriod, int jawOffset, int teethOffset, int lipsOffset)
		{
			return indicator.WisemanAlligator(input, jawPeriod, teethPeriod, lipsPeriod, jawOffset, teethOffset, lipsOffset);
		}
	}
	
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.WisemanAwesomeOscillator WisemanAwesomeOscillator()
		{
			return indicator.WisemanAwesomeOscillator(Input);
		}

		public Indicators.WisemanAwesomeOscillator WisemanAwesomeOscillator(ISeries<double> input )
		{
			return indicator.WisemanAwesomeOscillator(input);
		}
	}
	
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.WisemanFractal WisemanFractal(int strength, int triangleOffset)
		{
			return indicator.WisemanFractal(Input, strength, triangleOffset);
		}

		public Indicators.WisemanFractal WisemanFractal(ISeries<double> input , int strength, int triangleOffset)
		{
			return indicator.WisemanFractal(input, strength, triangleOffset);
		}
	}
	
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.OrderFlowCumulativeDelta OrderFlowCumulativeDelta(NinjaTrader.NinjaScript.Indicators.CumulativeDeltaType deltaType, NinjaTrader.NinjaScript.Indicators.CumulativeDeltaPeriod period, int sizeFilter)
		{
			return indicator.OrderFlowCumulativeDelta(Input, deltaType, period, sizeFilter);
		}

		public Indicators.OrderFlowCumulativeDelta OrderFlowCumulativeDelta(ISeries<double> input, NinjaTrader.NinjaScript.Indicators.CumulativeDeltaType deltaType, NinjaTrader.NinjaScript.Indicators.CumulativeDeltaPeriod period, int sizeFilter)
		{
			return indicator.OrderFlowCumulativeDelta(input, deltaType, period, sizeFilter);
		}
	}
	
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.OrderFlowMarketDepthMap OrderFlowMarketDepthMap(NinjaTrader.NinjaScript.Indicators.BaseVolumeRange baseRange, int maxRange, int minRange, NinjaTrader.NinjaScript.Indicators.OpacityDistribution opacityDistribution, int depthMargin, bool extendLastKnown, bool showBidAskLine)
		{
			return indicator.OrderFlowMarketDepthMap(Input, baseRange, maxRange, minRange, opacityDistribution, depthMargin, extendLastKnown, showBidAskLine);
		}

		public Indicators.OrderFlowMarketDepthMap OrderFlowMarketDepthMap(ISeries<double> input, NinjaTrader.NinjaScript.Indicators.BaseVolumeRange baseRange, int maxRange, int minRange, NinjaTrader.NinjaScript.Indicators.OpacityDistribution opacityDistribution, int depthMargin, bool extendLastKnown, bool showBidAskLine)
		{
			return indicator.OrderFlowMarketDepthMap(input, baseRange, maxRange, minRange, opacityDistribution, depthMargin, extendLastKnown, showBidAskLine);
		}
	}
	
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.OrderFlowVWAP OrderFlowVWAP(NinjaTrader.NinjaScript.Indicators.VWAPResolution resolution, Data.TradingHours tradingHoursInstance, NinjaTrader.NinjaScript.Indicators.VWAPStandardDeviations numStandardDeviations, double sD1Multiplier, double sD2Multiplier, double sD3Multiplier)
		{
			return indicator.OrderFlowVWAP(Input, resolution, tradingHoursInstance, numStandardDeviations, sD1Multiplier, sD2Multiplier, sD3Multiplier);
		}

		public Indicators.OrderFlowVWAP OrderFlowVWAP(ISeries<double> input, NinjaTrader.NinjaScript.Indicators.VWAPResolution resolution, Data.TradingHours tradingHoursInstance, NinjaTrader.NinjaScript.Indicators.VWAPStandardDeviations numStandardDeviations, double sD1Multiplier, double sD2Multiplier, double sD3Multiplier)
		{
			return indicator.OrderFlowVWAP(input, resolution, tradingHoursInstance, numStandardDeviations, sD1Multiplier, sD2Multiplier, sD3Multiplier);
		}
	}
	
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.OrderFlowTradeDetector OrderFlowTradeDetector(NinjaTrader.NinjaScript.Indicators.TradeDetectorBaseLargeVolumeOn baseLargeVolumeOn, int minimumVolumeForMarker, int maximumMarkerSize, NinjaTrader.NinjaScript.Indicators.TradeDetectorSizeBase baseMarkerSizeOn, bool hoverValues)
		{
			return indicator.OrderFlowTradeDetector(Input, baseLargeVolumeOn, minimumVolumeForMarker, maximumMarkerSize, baseMarkerSizeOn, hoverValues);
		}

		public Indicators.OrderFlowTradeDetector OrderFlowTradeDetector(ISeries<double> input, NinjaTrader.NinjaScript.Indicators.TradeDetectorBaseLargeVolumeOn baseLargeVolumeOn, int minimumVolumeForMarker, int maximumMarkerSize, NinjaTrader.NinjaScript.Indicators.TradeDetectorSizeBase baseMarkerSizeOn, bool hoverValues)
		{
			return indicator.OrderFlowTradeDetector(input, baseLargeVolumeOn, minimumVolumeForMarker, maximumMarkerSize, baseMarkerSizeOn, hoverValues);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.WoodiesCCI WoodiesCCI(int chopIndicatorWidth, int neutralBars, int period, int periodEma, int periodLinReg, int periodTurbo, int sideWinderLimit0, int sideWinderLimit1, int sideWinderWidth)
		{
			return indicator.WoodiesCCI(Input, chopIndicatorWidth, neutralBars, period, periodEma, periodLinReg, periodTurbo, sideWinderLimit0, sideWinderLimit1, sideWinderWidth);
		}

		public Indicators.WoodiesCCI WoodiesCCI(ISeries<double> input , int chopIndicatorWidth, int neutralBars, int period, int periodEma, int periodLinReg, int periodTurbo, int sideWinderLimit0, int sideWinderLimit1, int sideWinderWidth)
		{
			return indicator.WoodiesCCI(input, chopIndicatorWidth, neutralBars, period, periodEma, periodLinReg, periodTurbo, sideWinderLimit0, sideWinderLimit1, sideWinderWidth);
		}
	}

	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.WoodiesPivots WoodiesPivots(NinjaTrader.NinjaScript.Indicators.HLCCalculationModeWoodie priorDayHlc, int width)
		{
			return indicator.WoodiesPivots(Input, priorDayHlc, width);
		}

		public Indicators.WoodiesPivots WoodiesPivots(ISeries<double> input , NinjaTrader.NinjaScript.Indicators.HLCCalculationModeWoodie priorDayHlc, int width)
		{
			return indicator.WoodiesPivots(input, priorDayHlc, width);
		}
	}
	
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.WisemanAlligator WisemanAlligator(int jawPeriod, int teethPeriod, int lipsPeriod, int jawOffset, int teethOffset, int lipsOffset)
		{
			return indicator.WisemanAlligator(Input, jawPeriod, teethPeriod, lipsPeriod, jawOffset, teethOffset, lipsOffset);
		}

		public Indicators.WisemanAlligator WisemanAlligator(ISeries<double> input , int jawPeriod, int teethPeriod, int lipsPeriod, int jawOffset, int teethOffset, int lipsOffset)
		{
			return indicator.WisemanAlligator(input, jawPeriod, teethPeriod, lipsPeriod, jawOffset, teethOffset, lipsOffset);
		}
	}
	
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.WisemanAwesomeOscillator WisemanAwesomeOscillator()
		{
			return indicator.WisemanAwesomeOscillator(Input);
		}

		public Indicators.WisemanAwesomeOscillator WisemanAwesomeOscillator(ISeries<double> input )
		{
			return indicator.WisemanAwesomeOscillator(input);
		}
	}
	
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.WisemanFractal WisemanFractal(int strength, int triangleOffset)
		{
			return indicator.WisemanFractal(Input, strength, triangleOffset);
		}

		public Indicators.WisemanFractal WisemanFractal(ISeries<double> input , int strength, int triangleOffset)
		{
			return indicator.WisemanFractal(input, strength, triangleOffset);
		}
	}
	
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.OrderFlowCumulativeDelta OrderFlowCumulativeDelta(NinjaTrader.NinjaScript.Indicators.CumulativeDeltaType deltaType, NinjaTrader.NinjaScript.Indicators.CumulativeDeltaPeriod period, int sizeFilter)
		{
			return indicator.OrderFlowCumulativeDelta(Input, deltaType, period, sizeFilter);
		}

		public Indicators.OrderFlowCumulativeDelta OrderFlowCumulativeDelta(ISeries<double> input, NinjaTrader.NinjaScript.Indicators.CumulativeDeltaType deltaType, NinjaTrader.NinjaScript.Indicators.CumulativeDeltaPeriod period, int sizeFilter)
		{
			return indicator.OrderFlowCumulativeDelta(input, deltaType, period, sizeFilter);
		}
	}
	
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.OrderFlowMarketDepthMap OrderFlowMarketDepthMap(NinjaTrader.NinjaScript.Indicators.BaseVolumeRange baseRange, int maxRange, int minRange, NinjaTrader.NinjaScript.Indicators.OpacityDistribution opacityDistribution, int depthMargin, bool extendLastKnown, bool showBidAskLine)
		{
			return indicator.OrderFlowMarketDepthMap(Input, baseRange, maxRange, minRange, opacityDistribution, depthMargin, extendLastKnown, showBidAskLine);
		}

		public Indicators.OrderFlowMarketDepthMap OrderFlowMarketDepthMap(ISeries<double> input, NinjaTrader.NinjaScript.Indicators.BaseVolumeRange baseRange, int maxRange, int minRange, NinjaTrader.NinjaScript.Indicators.OpacityDistribution opacityDistribution, int depthMargin, bool extendLastKnown, bool showBidAskLine)
		{
			return indicator.OrderFlowMarketDepthMap(input, baseRange, maxRange, minRange, opacityDistribution, depthMargin, extendLastKnown, showBidAskLine);
		}
	}
	
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.OrderFlowVWAP OrderFlowVWAP(NinjaTrader.NinjaScript.Indicators.VWAPResolution resolution, Data.TradingHours tradingHoursInstance, NinjaTrader.NinjaScript.Indicators.VWAPStandardDeviations numStandardDeviations, double sD1Multiplier, double sD2Multiplier, double sD3Multiplier)
		{
			return indicator.OrderFlowVWAP(Input, resolution, tradingHoursInstance, numStandardDeviations, sD1Multiplier, sD2Multiplier, sD3Multiplier);
		}

		public Indicators.OrderFlowVWAP OrderFlowVWAP(ISeries<double> input, NinjaTrader.NinjaScript.Indicators.VWAPResolution resolution, Data.TradingHours tradingHoursInstance, NinjaTrader.NinjaScript.Indicators.VWAPStandardDeviations numStandardDeviations, double sD1Multiplier, double sD2Multiplier, double sD3Multiplier)
		{
			return indicator.OrderFlowVWAP(input, resolution, tradingHoursInstance, numStandardDeviations, sD1Multiplier, sD2Multiplier, sD3Multiplier);
		}
	}
	
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.OrderFlowTradeDetector OrderFlowTradeDetector(NinjaTrader.NinjaScript.Indicators.TradeDetectorBaseLargeVolumeOn baseLargeVolumeOn, int minimumVolumeForMarker, int maximumMarkerSize, NinjaTrader.NinjaScript.Indicators.TradeDetectorSizeBase baseMarkerSizeOn, bool hoverValues)
		{
			return indicator.OrderFlowTradeDetector(Input, baseLargeVolumeOn, minimumVolumeForMarker, maximumMarkerSize, baseMarkerSizeOn, hoverValues);
		}

		public Indicators.OrderFlowTradeDetector OrderFlowTradeDetector(ISeries<double> input, NinjaTrader.NinjaScript.Indicators.TradeDetectorBaseLargeVolumeOn baseLargeVolumeOn, int minimumVolumeForMarker, int maximumMarkerSize, NinjaTrader.NinjaScript.Indicators.TradeDetectorSizeBase baseMarkerSizeOn, bool hoverValues)
		{
			return indicator.OrderFlowTradeDetector(input, baseLargeVolumeOn, minimumVolumeForMarker, maximumMarkerSize, baseMarkerSizeOn, hoverValues);
		}
	}
}
