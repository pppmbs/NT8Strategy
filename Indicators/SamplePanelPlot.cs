#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
	public class SamplePanelPlot : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description							= @"Enter the description for your new custom Indicator here.";
				Name								= "SamplePanelPlot";
				Calculate							= Calculate.OnBarClose;
				IsOverlay							= false;
				DisplayInDataBox					= true;
				DrawOnPricePanel					= true;
				DrawHorizontalGridLines				= true;
				DrawVerticalGridLines				= true;
				PaintPriceMarkers					= true;
				ScaleJustification					= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive			= true;
			}
			else if (State == State.Configure)
			{
				AddPlot(Brushes.Blue, "Plot"); 
			}
		}

		protected override void OnBarUpdate()
		{
			//Grab the Panel Plot value for the current bar and plot it.		
			Plot[0] = Strategy.PanelPlot.GetValueAt(CurrentBar); 
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public NinjaTrader.NinjaScript.Strategies.SampleStrategyPlot Strategy
		{
			get;set;	
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> Plot
		{
			get { return Values[0]; }
		}
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private SamplePanelPlot[] cacheSamplePanelPlot;
		public SamplePanelPlot SamplePanelPlot()
		{
			return SamplePanelPlot(Input);
		}

		public SamplePanelPlot SamplePanelPlot(ISeries<double> input)
		{
			if (cacheSamplePanelPlot != null)
				for (int idx = 0; idx < cacheSamplePanelPlot.Length; idx++)
					if (cacheSamplePanelPlot[idx] != null &&  cacheSamplePanelPlot[idx].EqualsInput(input))
						return cacheSamplePanelPlot[idx];
			return CacheIndicator<SamplePanelPlot>(new SamplePanelPlot(), input, ref cacheSamplePanelPlot);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.SamplePanelPlot SamplePanelPlot()
		{
			return indicator.SamplePanelPlot(Input);
		}

		public Indicators.SamplePanelPlot SamplePanelPlot(ISeries<double> input )
		{
			return indicator.SamplePanelPlot(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.SamplePanelPlot SamplePanelPlot()
		{
			return indicator.SamplePanelPlot(Input);
		}

		public Indicators.SamplePanelPlot SamplePanelPlot(ISeries<double> input )
		{
			return indicator.SamplePanelPlot(input);
		}
	}
}

#endregion
