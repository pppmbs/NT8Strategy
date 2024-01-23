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
using System.IO;
using System.Net.Sockets;
using System.Net;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class TServerViewGenerator : Strategy
	{
		private string pathMktView;
		private StreamWriter swMkt = null; // Store marekt view, 0=Bear, 1=Neutral, 2=Bull

		// Macro Market Views
		enum MarketView
		{
			Buy,
			Sell,
			Hold
		};
		MarketView currMarketView = MarketView.Hold;

		// with MaketConfirmation flag set to true, T-Server Buy signal only when current bar Close_price > Open_price, vice versa for Sell signal
		static bool MaketConfirmation = true;

		private Socket tSender = null;
		private byte[] tBytes = new byte[1024];
		int tLineNo = 0;
		private static readonly int tPortNumber = 3883;
		private static readonly string hostName = "AITrader";
		private string tServerSignal = "1";


		private void ConnectTimeServer()
		{
			// Connect to Time Server  
			try
			{
				// Do not attempt connection if already connected
				if (tSender != null)
					return;

				// Establish the remote endpoint for the socket.  
				// connecting server on vPortNumber  
				IPHostEntry ipHostInfo = Dns.GetHostEntry(hostName);

				IPAddress ipAddress = ipHostInfo.AddressList[1]; // depending on the Wifi set up, this index may change accordingly
																 //IPAddress ipAddress = ipHostInfo.AddressList[3];
																 //ipAddress = ipAddress.MapToIPv4();
				IPEndPoint remoteEP = new IPEndPoint(ipAddress, tPortNumber);

				Print("ipHostInfo=" + ipHostInfo.HostName.ToString() + " ipAddress=" + ipAddress.ToString());

				// Create a TCP/IP  socket.  
				tSender = new Socket(ipAddress.AddressFamily,
					SocketType.Stream, ProtocolType.Tcp);

				// Connect the socket to the remote endpoint. Catch any errors.  
				try
				{
					tSender.Connect(remoteEP);

					Print(" ************ Socket connected to : " +
						tSender.RemoteEndPoint.ToString() + "*************");

					// set receive timeout 10 secs
					tSender.ReceiveTimeout = 10000;
					// set send timeout 10 secs
					tSender.SendTimeout = 10000;
				}
				catch (ArgumentNullException ane)
				{
					Print("Socket Connect Error: ArgumentNullException : " + ane.ToString());
				}
				catch (SocketException se)
				{
					Print("Socket Connect Error: SocketException : " + se.ToString());
				}
				catch (Exception e)
				{
					Print("Socket Connect Error: Unexpected exception : " + e.ToString());
				}
			}
			catch (Exception e)
			{
				Print(e.ToString());
			}
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"T-Server provides market views for Buy, Sell or Hold";
				Name = "TServerViewGenerator";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				IsFillLimitOnTouch = false;
				MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution = OrderFillResolution.Standard;
				Slippage = 0;
				StartBehavior = StartBehavior.WaitUntilFlat;
				TimeInForce = TimeInForce.Gtc;
				TraceOrders = false;
				RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling = StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade = 20;
				// Disable this property for performance gains in Strategy Analyzer optimizations
				// See the Help Guide for additional information
				IsInstantiatedOnEachOptimizationIteration = true;
			}
			else if (State == State.Configure)
			{
				// Output tab2
				PrintTo = PrintTo.OutputTab2;
			}
			else if (State == State.DataLoaded)
			{
				Print("State == State.DataLoaded");

				ConnectTimeServer();
			}
		}


		private void WriteMarketView(MarketView mktView)
		{
			pathMktView = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
			pathMktView = System.IO.Path.Combine(pathMktView, "Artista" + ".mkt");

			swMkt = File.CreateText(pathMktView); // Open the path for Market View
			switch (mktView)
			{
				case MarketView.Buy:
					swMkt.WriteLine("2");
					break;
				case MarketView.Sell:
					swMkt.WriteLine("0");
					break;
				default:
					swMkt.WriteLine("1");
					break;
			}
			swMkt.Close();
			swMkt.Dispose();
			swMkt = null;
		}



		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 0)
			{
				string bufString;

				Print("tLineNo=" + tLineNo.ToString());

				// Skip all previous day bars until second bar of the day
				if (!Bars.GetTime(CurrentBar).Date.ToString("dd/MM/yyyy").Equals(DateTime.Now.ToString("dd/MM/yyyy")))
					return;
				if (Bars.GetTime(CurrentBar).ToString("HHmm").Equals("0000"))
					return;

                // construct the string buffer to be sent to DLNN
                bufString = tLineNo.ToString() + ',' +
                    Bars.GetTime(CurrentBar - 1).ToString("HHmmss") + ',' + Bars.GetTime(CurrentBar).ToString("HHmmss") + ',' +
                    Bars.GetOpen(CurrentBar).ToString() + ',' + Bars.GetClose(CurrentBar).ToString() + ',' +
                    Bars.GetHigh(CurrentBar).ToString() + ',' + Bars.GetLow(CurrentBar).ToString() + ',' +
                    Bars.GetVolume(CurrentBar).ToString() + ',' +
                    SMA(9)[0].ToString() + ',' + SMA(20)[0].ToString() + ',' + SMA(50)[0].ToString() + ',' +
                    MACD(12, 26, 9).Diff[0].ToString() + ',' + RSI(14, 3)[0].ToString() + ',' +
                    Bollinger(2, 20).Lower[0].ToString() + ',' + Bollinger(2, 20).Upper[0].ToString() + ',' +
                    CCI(20)[0].ToString() + ',' +
                    Bars.GetHigh(CurrentBar).ToString() + ',' + Bars.GetLow(CurrentBar).ToString() + ',' +
                    Momentum(20)[0].ToString() + ',' +
                    DM(14).DiPlus[0].ToString() + ',' + DM(14).DiMinus[0].ToString() + ',' +
                    VROC(25, 3)[0].ToString() + ',' +
                    '0' + ',' + '0' + ',' + '0' + ',' + '0' + ',' + '0' + ',' +
                    '0' + ',' + '0' + ',' + '0' + ',' + '0' + ',' + '0';

                //Print(bufString);

                //Print("CurrentTimeBar = " + CurrentBar + ": " + "bufString = " + bufString);
				if (!Bars.IsFirstBarOfSession)
				{
					Print("CurrentTimeBar" +
								" Start time=" + Bars.GetTime(Bars.CurrentBar - 1).ToString("HHmmss") +
								" End time=" + Bars.GetTime(Bars.CurrentBar).ToString("HHmmss") +
								" Open=" + Bars.GetOpen(Bars.CurrentBar).ToString() +
								" Close=" + Bars.GetClose(Bars.CurrentBar).ToString() +
								" High=" + Bars.GetHigh(Bars.CurrentBar).ToString() +
								" Low=" + Bars.GetLow(Bars.CurrentBar).ToString() +
								" Volume=" + Bars.GetVolume(Bars.CurrentBar).ToString() +
								" SMA9=" + SMA(9)[0].ToString() +
								" SMA20=" + SMA(20)[0].ToString() +
								" SMA50=" + SMA(50)[0].ToString() +
								" MACD=" + MACD(12, 26, 9).Diff[0].ToString() +
								" RSI=" + RSI(14, 3)[0].ToString() +
								" Boll_Low=" + Bollinger(2, 20).Lower[0].ToString() +
								" Boll_Hi=" + Bollinger(2, 20).Upper[0].ToString() +
								" CCI=" + CCI(20)[0].ToString() +
								" Momentum=" + Momentum(20)[0].ToString() +
								" DiPlus=" + DM(14).DiPlus[0].ToString() +
								" DiMinus=" + DM(14).DiMinus[0].ToString() +
								" VROC=" + VROC(25, 3)[0].ToString());
				}

				byte[] msg = Encoding.UTF8.GetBytes(bufString);


				int tBytesSent;
				int tBytesRec;

				try
				{
					// Send the data through the socket.  
					tBytesSent = tSender.Send(msg);

					// Receive the response from the remote device.  
					tBytesRec = tSender.Receive(tBytes);
				}
				catch (SocketException ex)
				{
					Print("TServer Socket exception::" + ex.Message + " " + ex.ToString());
				}

				tServerSignal = System.Text.Encoding.UTF8.GetString(tBytes, 0, tBytes.Length).Split(',')[1];
				Print("Start time=" + Bars.GetTime(CurrentBar - 1).ToString("HHmmss") + " End time=" + Bars.GetTime(CurrentBar).ToString("HHmmss"));
				Print("OnBarUpdate, TServer response= <<< " + tServerSignal + " >>> Current Bar: Open=" + Bars.GetOpen(CurrentBar) + " Close=" + Bars.GetClose(CurrentBar) + " High=" + Bars.GetHigh(CurrentBar) + " Low=" + Bars.GetLow(CurrentBar));
				//Print("Time Server signal=" + tServerSignal);
				switch (tServerSignal[0])
				{
					case '0':
                        // sell
                        if (MaketConfirmation)
                        {
                            if (Bars.GetOpen(CurrentBar) > Bars.GetClose(CurrentBar))
                            {
                                currMarketView = MarketView.Sell;
                                PlaySound(@"C:\Program Files (x86)\NinjaTrader 8\sounds\glass_shatter_c.wav");
                            }
							else
                            {
								currMarketView = MarketView.Hold;
								PlaySound(@"C:\Program Files (x86)\NinjaTrader 8\sounds\ding.wav");
							}
                        }
                        else
                        {
                            currMarketView = MarketView.Sell;
                            PlaySound(@"C:\Program Files (x86)\NinjaTrader 8\sounds\glass_shatter_c.wav");
                        }
                        break;
					case '2':
						// buy
						if (MaketConfirmation)
						{
							if (Bars.GetOpen(CurrentBar) < Bars.GetClose(CurrentBar))
							{
								currMarketView = MarketView.Buy;
								PlaySound(@"C:\Program Files (x86)\NinjaTrader 8\sounds\bicycle_bell.wav");
							}
							else
                            {
								currMarketView = MarketView.Hold;
								PlaySound(@"C:\Program Files (x86)\NinjaTrader 8\sounds\ding.wav");
							}
						}
						else
						{
							currMarketView = MarketView.Buy;
							PlaySound(@"C:\Program Files (x86)\NinjaTrader 8\sounds\bicycle_bell.wav");
						}
						break;
					default:
						currMarketView = MarketView.Hold;
						PlaySound(@"C:\Program Files (x86)\NinjaTrader 8\sounds\ding.wav");
						break;
				}
				tLineNo++;


				WriteMarketView(currMarketView);
				Print(DateTime.Now + " Current T-Server View = {{{{{ " + currMarketView.ToString() + " }}}}} ");
			}
		}
	}
}
