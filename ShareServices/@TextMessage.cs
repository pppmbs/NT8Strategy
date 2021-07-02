// 
// Copyright (C) 2021, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Serialization;
#endregion

namespace NinjaTrader.NinjaScript.ShareServices
{
	public class TextMessage : ShareService, IPreconfiguredProvider
	{
		private object icon;

		/// <summary>
		/// This MUST be overridden for any custom service properties to be copied over when instances of the service are created
		/// </summary>
		/// <param name="ninjaScript"></param>
		public override void CopyTo(NinjaScript ninjaScript)
		{
			base.CopyTo(ninjaScript);

			// Recompiling NinjaTrader.Custom after a Share service has been added will cause the Type to change.
			//  Use reflection to set the appropriate properties, rather than casting ninjaScript to Mail.
			PropertyInfo[] props = ninjaScript.GetType().GetProperties();
			foreach (PropertyInfo pi in props)
			{
				if (pi.Name == "Email")
					pi.SetValue(ninjaScript, Email);
				else if (pi.Name == "MmsAddress")
					pi.SetValue(ninjaScript, MmsAddress);
				else if (pi.Name == "PhoneNumber")
					pi.SetValue(ninjaScript, PhoneNumber);
				else if (pi.Name == "SmsAddress")
					pi.SetValue(ninjaScript, SmsAddress);
			}
		}

		public override object Icon
		{
			get
			{
				if (icon == null)
					icon = System.Windows.Application.Current.TryFindResource("ShareIconSMS");
				return icon;
			}
		}


		public async override Task OnShare(string text, string imageFilePath)
		{
			ShareService mailService = null;

			lock (Core.Globals.GeneralOptions.ShareServices)
				mailService = Core.Globals.GeneralOptions.ShareServices.FirstOrDefault(s => s.GetType().Name == "Mail" && s.Name == Email);
			if (mailService == null)
			{
				LogAndPrint(typeof(Custom.Resource), "ShareTextMessageUnknownEmailService", new[] { Email }, Cbi.LogLevel.Error);
				return;
			}

			string address = string.Empty;

			if (!string.IsNullOrEmpty(SmsAddress) && string.IsNullOrEmpty(MmsAddress))
				address = PhoneNumber.ToString(CultureInfo.InvariantCulture) + SmsAddress;
			else if (string.IsNullOrEmpty(SmsAddress) && !string.IsNullOrEmpty(MmsAddress))
				address = PhoneNumber.ToString(CultureInfo.InvariantCulture) + MmsAddress;
			else if (!string.IsNullOrEmpty(SmsAddress) && !string.IsNullOrEmpty(MmsAddress))
			{
				if (string.IsNullOrEmpty(imageFilePath))
					address = PhoneNumber.ToString(CultureInfo.InvariantCulture) + SmsAddress;
				else
					address = PhoneNumber.ToString(CultureInfo.InvariantCulture) + MmsAddress;
			}

			ShareService liveClone = mailService.Clone() as ShareService;
			try
			{
				if (liveClone != null)
				{
					liveClone.SetState(State.Active);
					await liveClone.OnShare(text, imageFilePath, new[] { address, string.Empty });
					LogAndPrint(typeof(Custom.Resource), "ShareTextMessageSentSuccessfully", new[] { Name }, Cbi.LogLevel.Information);
				}
			}
			catch (Exception exp)
			{
				LogAndPrint(typeof(Custom.Resource), "ShareTextMessageErrorOnShare", new [] { liveClone.Name, exp.Message }, Cbi.LogLevel.Error);
			}
			finally
			{
				if (liveClone != null)
					liveClone.SetState(State.Finalized);
			}
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				CharacterLimit				= int.MaxValue;
				CharactersReservedPerMedia	= int.MaxValue;
				IsConfigured				= true;
				IsDefault					= false;
				IsImageAttachmentSupported	= true;
				Name						= Custom.Resource.ShareTextMessageName;
				Signature					= string.Empty;
				UseOAuth					= false;

				ShareService defaultEmail	= null;

				lock (Core.Globals.GeneralOptions.ShareServices)
					defaultEmail = Core.Globals.GeneralOptions.ShareServices.FirstOrDefault(s => s.GetType().Name == "Mail" && s.IsDefault);

				Email						= defaultEmail != null ? defaultEmail.Name : string.Empty;
				MmsAddress					= string.Empty;
				PhoneNumber					= 8005551234;
				SmsAddress					= string.Empty;

				PreconfiguredNames			= new List<string>
				{
					Custom.Resource.ShareTextMessagePreconfiguredManual,
					Custom.Resource.ShareTextMessagePreconfiguredVerizon,
					Custom.Resource.ShareTextMessagePreconfiguredAtt,
					Custom.Resource.ShareTextMessagePreconfiguredTMobile,
					Custom.Resource.ShareTextMessagePreconfiguredSprint
				};

				SelectedPreconfiguredSetting = PreconfiguredNames[0];
			}
		}

		#region IPreconfiguredProvider
		[XmlIgnore]
		public List<string> PreconfiguredNames { get; set; }
		public string SelectedPreconfiguredSetting { get; set; }

		public void ApplyPreconfiguredSettings(string name)
		{
			if (name == Custom.Resource.ShareTextMessagePreconfiguredVerizon)
			{
				SmsAddress = "@vtext.com";
				MmsAddress = "@vzwpix.com";
			}
			else if (name == Custom.Resource.ShareTextMessagePreconfiguredAtt)
			{
				SmsAddress = "@txt.att.net";
				MmsAddress = "@mms.att.net";
			}
			else if (name == Custom.Resource.ShareTextMessagePreconfiguredTMobile)
			{
				SmsAddress = "@tmomail.net";
				MmsAddress = "@tmomail.net";
			}
			else if (name == Custom.Resource.ShareTextMessagePreconfiguredSprint)
			{
				SmsAddress = "@messaging.sprintpcs.com";
				MmsAddress = "@pm.sprint.com";
			}
		}
		#endregion

		#region Properties
		[Required(ErrorMessageResourceName = "ShareTextMessageEmailRequired", ErrorMessageResourceType = typeof(Custom.Resource))]
		[TypeConverter(typeof(TextMessageEmailConverter))]
		[Display(ResourceType = typeof(Custom.Resource), Name = "ShareTextMessageEmail", GroupName = "ShareServiceParameters", Order = 5)]
		public string Email { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "ShareTextMessageMmsAddress", GroupName = "ShareServiceParameters", Order = 30)]
		public string MmsAddress { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "ShareTextMessagePhoneNumber", GroupName = "ShareServiceParameters", Order = 10)]
		[Range(minimum: 0, maximum: 999999999999999)]
		public long PhoneNumber { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "ShareTextMessageSmsAddress", GroupName = "ShareServiceParameters", Order = 20)]
		public string SmsAddress { get; set; }
		#endregion
	}

	public class TextMessageEmailConverter : StringConverter
	{
		public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
		{
			List<string> values = new List<string>();
			lock (Core.Globals.GeneralOptions.ShareServices)
				foreach (ShareService shareService in Core.Globals.GeneralOptions.ShareServices)
				{
					string name = shareService.GetType().Name;
					if (name == "Mail")
						values.Add(shareService.Name); // Add the name configured by user
				}

			return new StandardValuesCollection(values);
		}

		public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return true; }

		public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
	}
}
