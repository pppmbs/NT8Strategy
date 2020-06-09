// 
// Copyright (C) 2020, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

using Microsoft.Win32;
using NinjaTrader.Data;
#endregion

namespace NinjaTrader.NinjaScript.ImportTypes
{
	// Here is how an importer works in principle:
	// - it would iterate through a list of instrument (could be a minimum of 1 instrument)
	// - next the data source is read for each instrument which is: all data points for an instruments are read
	// - a data point consists of these attributes: Open, High, Low, Close, Time, Volume. Bid and Ask are optional
	public class TextImportType : ImportType
	{
		private readonly	char[]			quotes					= new[] {'"'};
		private				CultureInfo		cultureInfo;
		private				int				currentInstrumentIdx	= -1;
		private				bool			firstLine				= true;
		private				bool			hasBidAsk;
		private				bool			hasSubSecond;
		private 			bool 			isCryptoCurrency;
		private				StreamReader	reader;
		private				Regex			regex;
		private				string			separator				= string.Empty;

		public bool EndOfBarTimestamps
		{ get; set; }

		public string[] FileNames 
		{ get; set; }

		protected override void Dispose(bool isDisposing)
		{
			if (reader != null)
				reader.Dispose();
			reader = null;
		}

		// Get the next data point. 'HasValidDataPoint=TRUE' indicates that a valid data point was read.
		protected override void OnNextDataPoint()
		{
			if (reader == null)
				return;

			while (true)
			{
				DataPointString = reader.ReadLine();
				if (DataPointString == null)
				{
					reader.Close();
					reader = null;
					return;
				}

				DataPointString = DataPointString.Trim();
				if (DataPointString.Length == 0)
					continue;

				if (firstLine)
				{
					separator = string.Empty;
					foreach (string separatorTmp in new[] {";", ","})
						if (new Regex(string.Format(CultureInfo.InvariantCulture, "{0}\"?[^{1}]+\"?{2}", separatorTmp, separatorTmp, separatorTmp)).Match(DataPointString).Success)
						{
							separator = separatorTmp;
							break;
						}

					if (separator.Length == 0)
					{
						Cbi.Log.Process(typeof (Custom.Resource), "ImportTypeNinjaTraderFieldSeparatorNotIdentified", new object[] {Instrument.FullName}, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
						reader.Close();
						reader = null;
						throw new InvalidOperationException();
					}

					regex = new Regex(string.Format(CultureInfo.InvariantCulture, "\"?[^{0}]+\"?", separator));
					firstLine = false;
				}

				MatchCollection matches = regex.Matches(DataPointString);
				if (matches.Count == 0) // skip to next lines if current line is empty
					continue;

				// skip to next line if first char is not a digit
				string timeField = matches[0].Value.Trim(quotes).Trim().Replace("-", string.Empty).Replace(":", string.Empty).Replace(" ", string.Empty);
				if (timeField.Length == 0 || !char.IsDigit(timeField[0]))
					continue;

				if (matches.Count != 3 && matches.Count != 5 && matches.Count != 6)
				{
					Cbi.Log.Process(typeof (Custom.Resource), "ImportTypeNinjaTraderUnexpectedFieldNumber", new object[] {Instrument.FullName, NumberOfDataPoints}, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
					reader.Close();
					reader = null;
					throw new InvalidOperationException();
				}

				// set actual PeriodType on reading the first line of data
				if (NumberOfDataPoints == 0)
					/* for definition of format -> Data.Bars.SaveToFile/Export */
					if (matches.Count >= 6 && timeField.Length == 8)
						BarsPeriodType = BarsPeriodType.Day;
					else if (matches.Count >= 6 && (timeField.Length == 12 || timeField.Length == 14))
						BarsPeriodType = BarsPeriodType.Minute;
					else if (matches.Count == 3 && timeField.Length == 14)
					{
						BarsPeriodType	= BarsPeriodType.Tick;
						hasBidAsk		= false;
						hasSubSecond	= false;
					}
					else if (matches.Count == 3 && timeField.Length == 21)
					{
						BarsPeriodType	= BarsPeriodType.Tick;
						hasBidAsk		= false;
						hasSubSecond	= true;
					}
					else if (matches.Count == 5 && timeField.Length == 14)
					{
						BarsPeriodType	= BarsPeriodType.Tick;
						hasBidAsk		= true;
						hasSubSecond	= false;
					}
					else if (matches.Count == 5 && timeField.Length == 21)
					{
						BarsPeriodType		= BarsPeriodType.Tick;
						hasBidAsk		= true;
						hasSubSecond	= true;
					}

				Time = Core.Globals.MinDate;
				try
				{
					/* for definition of format -> Data.Bars.SaveToFile/Export */
					if (BarsPeriodType == BarsPeriodType.Day)
						Time = new DateTime(Convert.ToInt32(timeField.Substring(0, 4), CultureInfo.InvariantCulture),
											Convert.ToInt32(timeField.Substring(4, 2), CultureInfo.InvariantCulture),
											Convert.ToInt32(timeField.Substring(6, 2), CultureInfo.InvariantCulture));
					else if (BarsPeriodType == BarsPeriodType.Minute)
					{
						Time = new DateTime(Convert.ToInt32(timeField.Substring(0, 4), CultureInfo.InvariantCulture),
											Convert.ToInt32(timeField.Substring(4, 2), CultureInfo.InvariantCulture),
											Convert.ToInt32(timeField.Substring(6, 2), CultureInfo.InvariantCulture),
											Convert.ToInt32(timeField.Substring(8, 2), CultureInfo.InvariantCulture),
											Convert.ToInt32(timeField.Substring(10, 2), CultureInfo.InvariantCulture),
											0);
						if (!EndOfBarTimestamps)
							Time = Time.AddMinutes(1);
					}
					else if (BarsPeriodType == BarsPeriodType.Tick && !hasSubSecond)
						Time = new DateTime(Convert.ToInt32(timeField.Substring(0, 4), CultureInfo.InvariantCulture),
											Convert.ToInt32(timeField.Substring(4, 2), CultureInfo.InvariantCulture),
											Convert.ToInt32(timeField.Substring(6, 2), CultureInfo.InvariantCulture),
											Convert.ToInt32(timeField.Substring(8, 2), CultureInfo.InvariantCulture),
											Convert.ToInt32(timeField.Substring(10, 2), CultureInfo.InvariantCulture),
											Convert.ToInt32(timeField.Substring(12, 2), CultureInfo.InvariantCulture));
					else if (BarsPeriodType == BarsPeriodType.Tick && hasSubSecond)
						Time = new DateTime(new DateTime(Convert.ToInt32(timeField.Substring(0, 4), CultureInfo.InvariantCulture),
														Convert.ToInt32(timeField.Substring(4, 2), CultureInfo.InvariantCulture),
														Convert.ToInt32(timeField.Substring(6, 2), CultureInfo.InvariantCulture),
														Convert.ToInt32(timeField.Substring(8, 2), CultureInfo.InvariantCulture),
														Convert.ToInt32(timeField.Substring(10, 2), CultureInfo.InvariantCulture),
														Convert.ToInt32(timeField.Substring(12, 2), CultureInfo.InvariantCulture)).Ticks + Convert.ToInt32(timeField.Substring(14, 7), CultureInfo.InvariantCulture));
				}
				catch (Exception exp)
				{
					Cbi.Log.Process(typeof (Custom.Resource), "ImportTypeNinjaTraderDateTimeFormatError", new object[] {Instrument.FullName, NumberOfDataPoints, exp.Message, DataPointString}, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
					reader.Close();
					reader = null;
					throw new InvalidOperationException();
				}

				if (cultureInfo == null)
				{
					List<CultureInfo> cultureInfos = new List<CultureInfo>();
					CultureInfo tmp;

					try
					{
						tmp = new CultureInfo("en-US");
						cultureInfos.Add(tmp);
					}
					catch{}
					try
					{
						tmp = (CultureInfo) CultureInfo.CurrentCulture.Clone();
						cultureInfos.Add(tmp);
					}
					catch{}
					try
					{
						tmp = new CultureInfo("de-DE");
						cultureInfos.Add(tmp);
					}
					catch{}

					foreach (CultureInfo cultureInfoTmp in cultureInfos)
					{
						// turn off number grouping, since the number grouping character could be a decimal separator for a different culture
						cultureInfoTmp.NumberFormat.NumberGroupSeparator = string.Empty;

						try
						{
							Open		= Convert.ToDouble(matches[1].Value.Trim(quotes).Trim(), cultureInfoTmp);
							cultureInfo	= cultureInfoTmp;
							break;
						}
						catch
						{
						}
					}

					if (cultureInfo == null)
					{
						Cbi.Log.Process(typeof (Custom.Resource), "ImportTypeNinjaTraderNumericPriceFormatError", new object[] {Instrument.FullName}, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
						try
						{
							reader.Close();
						}
						catch{}
						reader = null;
						throw new InvalidOperationException();
					}
				}

				try
				{
					/* for definition of format -> Data.Bars.SaveToFile/Export */
					Open	= Convert.ToDouble(matches[1].Value.Trim(quotes).Trim(), cultureInfo);
					High	= (BarsPeriodType == BarsPeriodType.Tick ? Open : Convert.ToDouble(matches[2].Value.Trim(quotes).Trim(), cultureInfo));
					Low		= (BarsPeriodType == BarsPeriodType.Tick ? Open : Convert.ToDouble(matches[3].Value.Trim(quotes).Trim(), cultureInfo));
					Close	= (BarsPeriodType == BarsPeriodType.Tick ? Open : Convert.ToDouble(matches[4].Value.Trim(quotes).Trim(), cultureInfo));
					if (hasBidAsk)
					{
						Bid = (Convert.ToDouble(matches[2].Value.Trim(quotes).Trim(), cultureInfo));
						Ask = (Convert.ToDouble(matches[3].Value.Trim(quotes).Trim(), cultureInfo));
					}
					else
						Bid = Ask = double.MinValue;

					if (BarsPeriodType != BarsPeriodType.Tick)
						Volume = isCryptoCurrency ? Core.Globals.FromCryptocurrencyVolume(Convert.ToDouble(matches[5].Value.Trim(quotes).Trim(), cultureInfo)) : Convert.ToInt64(matches[5].Value.Trim(quotes).Trim(), cultureInfo);
					else if (BarsPeriodType == BarsPeriodType.Tick && !hasBidAsk)
						Volume = isCryptoCurrency ? Core.Globals.FromCryptocurrencyVolume(Convert.ToDouble(matches[2].Value.Trim(quotes).Trim(), cultureInfo)) : Convert.ToInt64(matches[2].Value.Trim(quotes).Trim(), cultureInfo);
					else // PeriodType == Data.PeriodType.Tick && hasBidAsk
						Volume = isCryptoCurrency ? Core.Globals.FromCryptocurrencyVolume(Convert.ToDouble(matches[4].Value.Trim(quotes).Trim(), cultureInfo)) : Convert.ToInt64(matches[4].Value.Trim(quotes).Trim(), cultureInfo);

					HasValidDataPoint = true;
					return;
				}
				catch (Exception exp)
				{
					Cbi.Log.Process(typeof (Custom.Resource), "ImportTypeNinjaTraderFormatError", new object[] {Instrument.FullName, NumberOfDataPoints, exp.Message, DataPointString}, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
					reader.Close();
					reader = null;
					throw new InvalidOperationException();
				}
				// keep going until finally 1 line of data was read
			}
		}

		// Get the next instrument to import. 'HasValidInstrument=TRUE' indicates that the source for the valid instrument could be read.
		protected override void OnNextInstrument()
		{
			if (FileNames == null)
				return;

			while (Instrument == null && currentInstrumentIdx + 1 < FileNames.Length)
			{
				FileInfo fileInfo		= new FileInfo(FileNames[++currentInstrumentIdx]);
				string instrumentName	= fileInfo.Name.ToLowerInvariant().Replace(".ask.", ".").Replace(".bid.", ".").Replace(".last.", ".");
				Instrument				= Cbi.Instrument.GetInstrument(fileInfo.Extension.Length == 4 && instrumentName.Length > fileInfo.Extension.Length
												? instrumentName.Substring(0, instrumentName.Length - fileInfo.Extension.Length).ToUpperInvariant()
												: instrumentName.ToUpperInvariant(), true);

				if (Instrument == null)
				{
					Cbi.Log.Process(typeof (Custom.Resource), "ImportTypeNinjaTraderInstrumentNotSupported", new object[] {FileNames[currentInstrumentIdx]}, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
					continue;
				}

				isCryptoCurrency 		= Instrument.MasterInstrument.InstrumentType == Cbi.InstrumentType.CryptoCurrency;

				try
				{
					reader = new StreamReader(FileNames[currentInstrumentIdx]);
				}
				catch (Exception exp)
				{
					Cbi.Log.Process(typeof (Custom.Resource), "ImportTypeNinjaTraderUnableReadData", new object[] {FileNames[currentInstrumentIdx], exp.Message}, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
					Instrument = null;
					continue;
				}

				cultureInfo			= null;
				firstLine			= true;
				HasValidInstrument	= true;
			}
		}

		protected override void OnStateChange()
		{
			switch (State)
			{
				case State.SetDefaults:
					EndOfBarTimestamps	= true;
					Name				= Custom.Resource.ImportTypeNinjaTraderEndOfBar;
					break;
				case State.Configure:
				{
					if (FileNames != null) 
						return;
					
					OpenFileDialog dialog = new OpenFileDialog()
					{
						FileName			= Custom.Resource.FileName,
						Filter				= Custom.Resource.FileFilterAnyWinForms,
						InitialDirectory	= Core.RecentFolders.GetRecentFolder("HistoryImport", Environment.GetFolderPath(Environment.SpecialFolder.Personal)),
						Multiselect			= true,
						Title				= Custom.Resource.Load
					};
	
					if (dialog.ShowDialog() != true) 
					{
						SetState(State.Terminated);
						return;
					}

					if (dialog.FileNames.Length <= 0) 
					{
						SetState(State.Terminated);
						return;
					}
					Core.RecentFolders.SetRecentFolder("HistoryImport", Path.GetDirectoryName(dialog.FileNames[0]));
					FileNames = dialog.FileNames;
				}
					break;
				case State.Terminated:
					Dispose(true);
					break;
			}
		}
	}
}