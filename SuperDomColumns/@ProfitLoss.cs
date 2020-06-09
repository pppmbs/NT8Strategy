// 
// Copyright (C) 2020, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using NinjaTrader.Gui.SuperDom;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
#endregion

namespace NinjaTrader.NinjaScript.SuperDomColumns
{
	public class ProfitLoss : SuperDomColumn
	{
		private FontFamily		fontFamily;	
		private CultureInfo		forexCulture		= null;
		private Pen				gridPen;		
		private double			halfPenWidth;
		private Typeface		typeFace;

		[XmlIgnore]
		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptColumnBaseBackground", GroupName = "PropertyCategoryVisual", Order = 105)]
		public Brush BackColor
		{ get; set; }

		[Browsable(false)]
		public string BackColorSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(BackColor, "brushPriceColumnBackground"); }
			set { BackColor = NinjaTrader.Gui.Serialize.StringToBrush(value, "brushPriceColumnBackground"); }
		}
	
		[XmlIgnore]
		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptNegativeBackgroundColor", GroupName = "PropertyCategoryVisual", Order = 110)]
		public Brush NegativeBackColor
		{ get; set; }

		[Browsable(false)]
		public string NegativeBackColorSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(NegativeBackColor); }
			set { NegativeBackColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptNegativeForegroundColor", GroupName = "PropertyCategoryVisual", Order = 120)]
		public Brush NegativeForeColor
		{ get; set; }

		[Browsable(false)]
		public string NegativeForeColorSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(NegativeForeColor); }
			set { NegativeForeColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptPositiveBackgroundColor", GroupName = "PropertyCategoryVisual", Order = 130)]
		public Brush PositiveBackColor
		{ get; set; }

		[Browsable(false)]
		public string PositiveBackColorSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(PositiveBackColor); }
			set { PositiveBackColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptPositiveForegroundColor", GroupName = "PropertyCategoryVisual", Order = 140)]
		public Brush PositiveForeColor
		{ get; set; }

		[Browsable(false)]
		public string PositiveForeColorSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(PositiveForeColor); }
			set { PositiveForeColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
		}

		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptDisplayUnit", GroupName = "NinjaScriptSetup", Order = 100)]
		public Cbi.PerformanceUnit PnlDisplayUnit { get; set; }

		protected override void OnRender(DrawingContext dc, double renderWidth)
		{
			// This may be true if the UI for a column hasn't been loaded yet (e.g., restoring multiple tabs from workspace won't load each tab until it's clicked by the user)
			if (gridPen == null)
			{
				if (UiWrapper != null && PresentationSource.FromVisual(UiWrapper) != null)
				{ 
					Matrix m			= PresentationSource.FromVisual(UiWrapper).CompositionTarget.TransformToDevice;
					double dpiFactor	= 1 / m.M11;
					gridPen				= new Pen(Application.Current.TryFindResource("BorderThinBrush") as Brush,  1 * dpiFactor);
					halfPenWidth		= gridPen.Thickness * 0.5;
				}
			}

			double verticalOffset = -gridPen.Thickness;

			lock (SuperDom.Rows)
				foreach (PriceRow row in SuperDom.Rows)
				{
					if (renderWidth - halfPenWidth >= 0)
					{
						// Draw a cell
						Rect rect = new Rect(-halfPenWidth, verticalOffset, renderWidth - halfPenWidth, SuperDom.ActualRowHeight);

						// Create a guidelines set
						GuidelineSet guidelines = new GuidelineSet();
						guidelines.GuidelinesX.Add(rect.Left	+ halfPenWidth);
						guidelines.GuidelinesX.Add(rect.Right	+ halfPenWidth);
						guidelines.GuidelinesY.Add(rect.Top		+ halfPenWidth);
						guidelines.GuidelinesY.Add(rect.Bottom	+ halfPenWidth);

						dc.PushGuidelineSet(guidelines);

						if (SuperDom.IsConnected && SuperDom.Position != null && SuperDom.Position.MarketPosition != Cbi.MarketPosition.Flat)
						{ 
							double	pnL				= SuperDom.Position.GetUnrealizedProfitLoss(PnlDisplayUnit, row.Price);
							string	pnlString		= string.Empty;
							switch (PnlDisplayUnit)
							{
								case Cbi.PerformanceUnit.Currency	:	pnlString = Core.Globals.FormatCurrency(pnL, SuperDom.Position);					break;
								case Cbi.PerformanceUnit.Percent	:	pnlString = pnL.ToString("P", Core.Globals.GeneralOptions.CurrentCulture);			break;
								case Cbi.PerformanceUnit.Pips		:	pnlString = (Math.Round(pnL * 10) / 10.0).ToString("0.0", forexCulture);			break;
								case Cbi.PerformanceUnit.Points		:	pnlString = SuperDom.Position.Instrument.MasterInstrument.RoundToTickSize(pnL).ToString("0.#######", Core.Globals.GeneralOptions.CurrentCulture); break;
								case Cbi.PerformanceUnit.Ticks		:	pnlString = Math.Round(pnL).ToString(Core.Globals.GeneralOptions.CurrentCulture);	break;
							}

							dc.DrawRectangle(pnL > 0 ? PositiveBackColor : NegativeBackColor, null, rect);
							dc.DrawLine(gridPen, new Point(-gridPen.Thickness, rect.Bottom), new Point(renderWidth - halfPenWidth, rect.Bottom));
							dc.DrawLine(gridPen, new Point(rect.Right, verticalOffset), new Point(rect.Right, rect.Bottom));
						
							// Print PnL value - remember to set MaxTextWidth so text doesn't spill into another column
							fontFamily				= SuperDom.Font.Family;
							typeFace				= new Typeface(fontFamily, SuperDom.Font.Italic ? FontStyles.Italic : FontStyles.Normal, SuperDom.Font.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
							
							if (renderWidth - 6 > 0)
							{
								FormattedText pnlText = new FormattedText(pnlString, Core.Globals.GeneralOptions.CurrentCulture, FlowDirection.LeftToRight, typeFace, SuperDom.Font.Size, SuperDom.Position.Instrument.MasterInstrument.RoundToTickSize(pnL) > 0 ? PositiveForeColor : NegativeForeColor) { MaxLineCount = 1, MaxTextWidth = renderWidth - 6, Trimming = TextTrimming.CharacterEllipsis };
								dc.DrawText(pnlText, new Point(4, verticalOffset + (SuperDom.ActualRowHeight - pnlText.Height) / 2));
							}
						}
						else
						{
							dc.DrawRectangle(BackColor, null, rect);
							dc.DrawLine(gridPen, new Point(-gridPen.Thickness, rect.Bottom), new Point(renderWidth - halfPenWidth, rect.Bottom));
							dc.DrawLine(gridPen, new Point(rect.Right, verticalOffset), new Point(rect.Right, rect.Bottom));
						}
				
						dc.Pop();
						verticalOffset += SuperDom.ActualRowHeight;
					}
				}
		}
		
		protected override void OnStateChange()
		{
			if (State == NinjaTrader.NinjaScript.State.SetDefaults)
			{
				Name					= NinjaTrader.Custom.Resource.NinjaScriptSuperDomColumnProfitAndLoss;
				Description				= NinjaTrader.Custom.Resource.NinjaScriptSuperDomColumnDescriptionPnl;
				DefaultWidth			= 100;
				PreviousWidth			= -1;
				IsDataSeriesRequired	= false;
				BackColor				= Application.Current.TryFindResource("brushPriceColumnBackground") as Brush;
				NegativeBackColor		= Brushes.Crimson;
				NegativeForeColor		= Application.Current.TryFindResource("FontControlBrush") as Brush;
				PositiveBackColor		= Brushes.SeaGreen;
				PositiveForeColor		= Application.Current.TryFindResource("FontControlBrush") as Brush;

				PnlDisplayUnit			= Cbi.PerformanceUnit.Currency;
				forexCulture			= Core.Globals.GeneralOptions.CurrentCulture.Clone() as CultureInfo;
				if (forexCulture != null)
					forexCulture.NumberFormat.NumberDecimalSeparator = "'";
			}
			else if (State == State.Configure)
			{
				if (UiWrapper != null && PresentationSource.FromVisual(UiWrapper) != null)
				{ 
					Matrix m			= PresentationSource.FromVisual(UiWrapper).CompositionTarget.TransformToDevice;
					double dpiFactor	= 1 / m.M11;
					gridPen				= new Pen(Application.Current.TryFindResource("BorderThinBrush") as Brush,  1 * dpiFactor);
					halfPenWidth		= gridPen.Thickness * 0.5;
				}
			}
		}
	}
}