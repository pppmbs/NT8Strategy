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
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class SampleStrategyPlot : Strategy
	{
		private SampleOverlayPlot sampleOverlayPlot;
		private SamplePanelPlot samplePanelPlot;
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description							= @"Enter the description for your new custom Strategy here.";
				Name								= "SampleStrategyPlot";
				Calculate							= Calculate.OnBarClose;
				EntriesPerDirection					= 1;
				EntryHandling						= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy		= true;
				ExitOnSessionCloseSeconds			= 30;
				IsFillLimitOnTouch					= false;
				MaximumBarsLookBack					= MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution					= OrderFillResolution.Standard;
				Slippage							= 0;
				StartBehavior						= StartBehavior.WaitUntilFlat;
				TimeInForce							= TimeInForce.Gtc;
				TraceOrders							= false;
				RealtimeErrorHandling				= RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling					= StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade					= 20;
			}
			else if (State == State.Configure)
			{
				// Data Series which we will store the values we want to plot per bar. 
				OverlayPlot = new Series<double>(this); 
				PanelPlot = new Series<double>(this); 
				
				// Create an indicator which is set to overlayplot. The indicator will use this strategies references OverlayPlot series to plot. 
				sampleOverlayPlot = SampleOverlayPlot(); 
				sampleOverlayPlot.Strategy = this;
				AddChartIndicator(sampleOverlayPlot); 
				
				// Create an indicator which is set for a panel plot. The indicator will use this strategies references PanelPlot series to plot. 
				samplePanelPlot = SamplePanelPlot();
				samplePanelPlot.Strategy = this;
				AddChartIndicator(samplePanelPlot); 
			}
		}

		protected override void OnBarUpdate()
		{							
			OverlayPlot[0] = Close[0];
			PanelPlot[0] = Low[0];
			
			double dummyValueOne = samplePanelPlot[0];   // used to call indicators OBU historically
			double dummyValueTwo = sampleOverlayPlot[0]; // used to call indicators OBU historically
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> OverlayPlot;
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PanelPlot;
	}	
}
