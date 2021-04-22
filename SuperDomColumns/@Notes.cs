// 
// Copyright (C) 2021, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using NinjaTrader.Gui.SuperDom;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
#endregion

namespace NinjaTrader.NinjaScript.SuperDomColumns
{
	public class Notes : SuperDomColumn
	{
		private double		columnWidth;
		private double		currentEditingPrice	= -1.0;
		private FontFamily	fontFamily;	
		private double		gridHeight;
		private int			gridIndex;
		private Pen			gridPen;		
		private double		halfPenWidth;
		private TextBox		tbNotes;
		private Typeface	typeFace;

		#region Mouse Input Handling
		private CommandBinding			displayTextBoxCommandBinding;
		private MouseBinding			doubleClickMouseBinding;

		public static ICommand			DisplayTextBox	= new RoutedCommand("DisplayTextBox", typeof(Notes));
		public static void				DisplayTextBoxExecuted(object sender, ExecutedRoutedEventArgs e)
		{
			Notes notesCol = e.Parameter as Notes;
			if (notesCol == null) return;

			Point mousePos = Mouse.GetPosition(e.Source as IInputElement);

			// Use the mouse position Y coordinate and maths to determine where in the grid of notes cells we are,
			// then update the position of the textbox and display it
			if (notesCol.gridHeight > 0 && notesCol.SuperDom.IsConnected)
			{
				if (notesCol.tbNotes.Visibility == Visibility.Visible)
				{
					// Commit value if the user double clicks away from the text box
					notesCol.SetAndSaveNote();
					notesCol.tbNotes.Text = string.Empty;
				}

				notesCol.gridIndex					= (int)Math.Floor(mousePos.Y / notesCol.SuperDom.ActualRowHeight);
				notesCol.currentEditingPrice		= notesCol.SuperDom.Rows[notesCol.gridIndex].Price;
				
				double	tbOffset					= notesCol.gridIndex * notesCol.SuperDom.ActualRowHeight;
				
				notesCol.tbNotes.Height				= notesCol.SuperDom.ActualRowHeight;
				notesCol.tbNotes.Margin				= new Thickness(0, tbOffset, 0, 0);				
				notesCol.tbNotes.Text				= notesCol.PriceStringValues[notesCol.currentEditingPrice];
				notesCol.tbNotes.Width				= notesCol.columnWidth;
				notesCol.tbNotes.Visibility			= Visibility.Visible;
				notesCol.tbNotes.SetValue(Panel.ZIndexProperty, 100);
				notesCol.tbNotes.BringIntoView();
				notesCol.tbNotes.Focus();

				notesCol.OnPropertyChanged();
			}
		}
		#endregion

		[XmlIgnore]
		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptColumnBaseBackground", GroupName = "PropertyCategoryVisual", Order = 110)]
		public Brush BackColor
		{ get; set; }

		[Browsable(false)]
		public string BackBrushSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(BackColor, "brushPriceColumnBackground"); }
			set { BackColor = NinjaTrader.Gui.Serialize.StringToBrush(value, "brushPriceColumnBackground"); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptColumnBaseForeground", GroupName = "PropertyCategoryVisual", Order = 111)]
		public Brush ForeColor
		{ get; set; }

		[Browsable(false)]
		public string ForeColorSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(ForeColor); }
			set { ForeColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
		}

		public override void CopyCustomData(SuperDomColumn newInstance)
		{
			Notes newNotes = newInstance as Notes;
			if (newNotes == null) return;

			newNotes.PriceStringValues = new ConcurrentDictionary<double, string>(PriceStringValues);
		}

		[Browsable(false)]
		public List<string> NotesSerializable { get; set; }

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

			columnWidth				= renderWidth;
			gridHeight				= -gridPen.Thickness;
			double verticalOffset	= -gridPen.Thickness;
			double pixelsPerDip		= VisualTreeHelper.GetDpi(UiWrapper).PixelsPerDip;

			// If SuperDom scrolls so that editing price goes off the grid, hide the textbox until the editing price is visible again
			if (SuperDom.IsConnected)
			{
				if (tbNotes.Visibility == Visibility.Visible && SuperDom.Rows.All(r => r.Price != currentEditingPrice))
					tbNotes.Visibility = Visibility.Hidden;
				if (tbNotes.Visibility == Visibility.Hidden && SuperDom.Rows.Any(r => r.Price == currentEditingPrice))
					tbNotes.Visibility = Visibility.Visible;
			}

			lock (SuperDom.Rows)
				foreach (PriceRow row in SuperDom.Rows)
				{
					// Add new prices if needed to the dictionary as the user scrolls
					PriceStringValues.AddOrUpdate(row.Price, string.Empty, (key, oldValue) => oldValue);
					// If textbox is open, move it when the SuperDom scrolls
					if (tbNotes.Visibility == Visibility.Visible && row.Price == currentEditingPrice)
					{
						if (SuperDom.Rows.IndexOf(row) != gridIndex)
						{
							gridIndex			= SuperDom.Rows.IndexOf(row);
							double tbOffset		= gridIndex * SuperDom.ActualRowHeight;
							tbNotes.Margin		= new Thickness(0, tbOffset, 0, 0);
						}
					}

					// Draw cell
					if (renderWidth - halfPenWidth >= 0)
					{
						Rect rect = new Rect(-halfPenWidth, verticalOffset, renderWidth - halfPenWidth, SuperDom.ActualRowHeight);

						// Create a guidelines set
						GuidelineSet guidelines = new GuidelineSet();
						guidelines.GuidelinesX.Add(rect.Left	+ halfPenWidth);
						guidelines.GuidelinesX.Add(rect.Right	+ halfPenWidth);
						guidelines.GuidelinesY.Add(rect.Top		+ halfPenWidth);
						guidelines.GuidelinesY.Add(rect.Bottom	+ halfPenWidth);

						dc.PushGuidelineSet(guidelines);
						dc.DrawRectangle(BackColor, null, rect);
						dc.DrawLine(gridPen, new Point(-gridPen.Thickness, rect.Bottom), new Point(renderWidth - halfPenWidth, rect.Bottom));
						dc.DrawLine(gridPen, new Point(rect.Right, verticalOffset), new Point(rect.Right, rect.Bottom));
						// Print note value - remember to set MaxTextWidth so text doesn't spill into another column
						string note;
						if (PriceStringValues.TryGetValue(row.Price, out note) && !string.IsNullOrEmpty(PriceStringValues[row.Price]))
						{
							fontFamily				= SuperDom.Font.Family;
							typeFace				= new Typeface(fontFamily, SuperDom.Font.Italic ? FontStyles.Italic : FontStyles.Normal, SuperDom.Font.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);

							if (renderWidth - 6 > 0)
							{
								FormattedText noteText = new FormattedText(note, Core.Globals.GeneralOptions.CurrentCulture, FlowDirection.LeftToRight, typeFace, SuperDom.Font.Size, ForeColor, pixelsPerDip) { MaxLineCount = 1, MaxTextWidth = renderWidth - 6, Trimming = TextTrimming.CharacterEllipsis };
								dc.DrawText(noteText, new Point(0 + 4, verticalOffset + (SuperDom.ActualRowHeight - noteText.Height) / 2));
							}
						}

						dc.Pop();
						verticalOffset	+= SuperDom.ActualRowHeight;
						gridHeight		+= SuperDom.ActualRowHeight;
					}
				}
		}

		public override void OnRestoreValues()
		{
			bool			restored		= false;

			if (NotesSerializable != null)
				foreach (string note in NotesSerializable)
				{
					string[]	noteVal		= note.Split(';');
					double		price;
					if (double.TryParse(noteVal[0], NumberStyles.Any, CultureInfo.InvariantCulture, out price))
					{
						PriceStringValues.AddOrUpdate(price, noteVal[1], (key, old) => noteVal[1]);
						restored = true;
					}
				}

			if (restored) OnPropertyChanged();
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name							= NinjaTrader.Custom.Resource.NinjaScriptSuperDomColumnNotes;
				Description						= NinjaTrader.Custom.Resource.NinjaScriptSuperDomColumnDescriptionNotes;
				DefaultWidth					= 160;
				PreviousWidth					= -1;
				IsDataSeriesRequired			= false;
				BackColor						= Application.Current.TryFindResource("brushPriceColumnBackground") as Brush;
				ForeColor						= Application.Current.TryFindResource("FontControlBrush") as Brush;

				NotesSerializable				= new List<string>();
				PriceStringValues				= new ConcurrentDictionary<double, string>();
			}
			else if (State == State.Configure)
			{
				if (UiWrapper != null && PresentationSource.FromVisual(UiWrapper) != null)
				{
					Matrix m					= PresentationSource.FromVisual(UiWrapper).CompositionTarget.TransformToDevice;
					double dpiFactor			= 1 / m.M11;
					gridPen						= new Pen(Application.Current.TryFindResource("BorderThinBrush") as Brush,  1 * dpiFactor);
					halfPenWidth				= gridPen.Thickness * 0.5;
				}

				tbNotes	= new TextBox{
										Margin				= new Thickness(0), 
										VerticalAlignment	= VerticalAlignment.Top, 
										Visibility			= Visibility.Hidden
									};

				SetBindings();

				tbNotes.LostKeyboardFocus += (o, args) =>
					{
						if (currentEditingPrice != -1.0 && tbNotes.Visibility == Visibility.Visible)
						{
							SetAndSaveNote();
	
							tbNotes.Text		= string.Empty;
							currentEditingPrice = -1.0;
							tbNotes.Visibility	= Visibility.Hidden;
							OnPropertyChanged();
						}
					};

				tbNotes.KeyDown += (o, args) =>
					{
						if (args.Key == Key.Enter || args.Key == Key.Tab)
						{
							SetAndSaveNote();

							tbNotes.Text		= string.Empty;
							currentEditingPrice	= -1.0;
							tbNotes.Visibility	= Visibility.Hidden;
							OnPropertyChanged();
						}
						else if (args.Key == Key.Escape)
						{
							currentEditingPrice	= -1.0;
							tbNotes.Visibility	= Visibility.Hidden;
							OnPropertyChanged();
						}
					};
			}
			else if (State == State.Active)
			{
				foreach (PriceRow row in SuperDom.Rows)
					PriceStringValues.AddOrUpdate(row.Price, string.Empty, (key, oldValue) => oldValue);
			}
			else if (State == State.Terminated)
			{
				if (UiWrapper != null)
				{
					UiWrapper.Children.Remove(tbNotes);
					UiWrapper.InputBindings.Remove(doubleClickMouseBinding);
					UiWrapper.CommandBindings.Remove(displayTextBoxCommandBinding);
				}
			}
		}

		[XmlIgnore]
		[Browsable(false)]
		public ConcurrentDictionary<double, string> PriceStringValues { get; set; }

		public override void SetBindings()
		{
			//Use InputBindings to handle mouse interactions
			//	MouseAction.LeftClick
			//	MouseAction.LeftDoubleClick
			//	MouseAction.MiddleClick
			//	MouseAction.MiddleDoubleClick
			//	MouseAction.None
			//	MouseAction.RightClick
			//	MouseAction.RightDoubleClick
			//	MouseAction.WheelClick
			doubleClickMouseBinding			= new MouseBinding(DisplayTextBox, new MouseGesture(MouseAction.LeftDoubleClick)) { CommandParameter = this };
			displayTextBoxCommandBinding	= new CommandBinding(DisplayTextBox, DisplayTextBoxExecuted);

			if (UiWrapper != null)
			{
				UiWrapper.InputBindings.Add(doubleClickMouseBinding);
				UiWrapper.CommandBindings.Add(displayTextBoxCommandBinding);
				UiWrapper.Children.Add(tbNotes);
			}
		}

		private void SetAndSaveNote()
		{
			string updatedValue = PriceStringValues.AddOrUpdate(currentEditingPrice, tbNotes.Text, (key, oldValue) => tbNotes.Text);
			lock (NotesSerializable)
			{
				if (NotesSerializable.Any(n => n.StartsWith(currentEditingPrice.ToString("N2", CultureInfo.InvariantCulture))))
				{
					int index = NotesSerializable.IndexOf(NotesSerializable.SingleOrDefault(n => n.StartsWith(currentEditingPrice.ToString("N2", CultureInfo.InvariantCulture))));
					NotesSerializable[index] = string.Format("{0};{1}", currentEditingPrice.ToString("N2", CultureInfo.InvariantCulture), updatedValue);
				}
				else
					NotesSerializable.Add(string.Format("{0};{1}", currentEditingPrice.ToString("N2", CultureInfo.InvariantCulture), tbNotes.Text));
			}
		}
	}
}