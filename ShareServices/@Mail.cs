// 
// Copyright (C) 2021, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Serialization;
#endregion

namespace NinjaTrader.NinjaScript.ShareServices
{
	public class Mail : ShareService, IPreconfiguredProvider
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
				if (pi.Name == "FromMailAddress")
					pi.SetValue(ninjaScript, FromMailAddress);
				if (pi.Name == "SenderDisplayName")
					pi.SetValue(ninjaScript, SenderDisplayName);
				else if (pi.Name == "IsBodyHtml")
					pi.SetValue(ninjaScript, IsBodyHtml);
				else if (pi.Name == "Password")
					pi.SetValue(ninjaScript, Password);
				else if (pi.Name == "Port")
					pi.SetValue(ninjaScript, Port);
				else if (pi.Name == "Server")
					pi.SetValue(ninjaScript, Server);
				else if (pi.Name == "Subject")
					pi.SetValue(ninjaScript, Subject);
				else if (pi.Name == "ToMailAddress")
					pi.SetValue(ninjaScript, ToMailAddress);
				else if (pi.Name == "UserName")
					pi.SetValue(ninjaScript, UserName);
				else if (pi.Name == "UseSSL")
					pi.SetValue(ninjaScript, UseSSL);
			}
		}

		public override object Icon
		{
			get
			{
				if (icon == null)
					icon = System.Windows.Application.Current.TryFindResource("ShareIconEmail");
				return icon;
			}
		}

		public async override Task OnShare(string text, string imageFilePath)
		{
			string[] attachmentPaths = null;
			if (File.Exists(imageFilePath))
				attachmentPaths = new[] { imageFilePath };

			string mailPassword	= Decrypt(Password);
			string mailUserName = Decrypt(UserName);

			if (Server.Trim().Length == 0 || Port == 0 || mailUserName.Trim().Length == 0 || mailPassword.Trim().Length == 0)
			{
				Cbi.Log.Process(typeof(Resource), "CoreGlobalsSendMail", null, Cbi.LogLevel.Error, Cbi.LogCategories.Default); // Don't do LogLevel.Alert or you'll get a recursive call to email the alert
				return;
			}

			List<FileStream>	attachmentStreams	= new List<FileStream>();
			MailMessage			smtpMail			= new MailMessage
														{
															Body		= text,
															From		= new MailAddress(FromMailAddress, SenderDisplayName),
															IsBodyHtml	= IsBodyHtml,
															Subject		= Subject,
														};

			foreach (string tmp in ToMailAddress.Split(new char[] { ',', ';' }))
			{
				MailAddress to;
				try
				{
					to = new MailAddress(tmp);
					smtpMail.To.Add(to);
				}
				catch (FormatException) { continue; }
			}

			if (!string.IsNullOrEmpty(imageFilePath))
				foreach (string attachmentPath in attachmentPaths)
					if (!string.IsNullOrEmpty(attachmentPath))
					{
						string tmpFile = string.Format(Core.Globals.GeneralOptions.CurrentCulture, @"{0}tmp\{1}", Core.Globals.UserDataDir, Guid.NewGuid().ToString("N"));
						File.Copy(attachmentPath, tmpFile);

						FileStream fs = new FileStream(tmpFile, FileMode.Open, FileAccess.Read);
						attachmentStreams.Add(fs);
						smtpMail.Attachments.Add(new Attachment(fs, new FileInfo(attachmentPath).Name));
					}

			try
			{
				using (SmtpClient smtp = new SmtpClient(Server, Port))
				{
					smtp.EnableSsl				= UseSSL;
					smtp.Timeout				= 120 * 1000;
					smtp.UseDefaultCredentials	= false;
					smtp.Credentials			= new NetworkCredential(mailUserName, mailPassword);
					smtp.SendCompleted += (o, e) =>
						{
							if (e.Error != null)
								LogAndPrint(typeof(Custom.Resource), "ShareMailSendError", new[] { e.Error.Message }, Cbi.LogLevel.Error);
							else
								LogAndPrint(typeof(Custom.Resource), "ShareMailSentSuccessfully", new[] { Name }, Cbi.LogLevel.Information);

							foreach (FileStream fs in attachmentStreams)
							{
								string tmp = fs.Name;
								fs.Close();
								if (File.Exists(tmp)) File.Delete(tmp);
							}
						};
				
					await smtp.SendMailAsync(smtpMail);
				}
			}
			catch (SmtpException ex)
			{
				Exception innerEx = ex.InnerException;
				string error = ex.Message;
				while (innerEx != null)
				{
					error += " " + innerEx.Message;
					innerEx = innerEx.InnerException;
				}

				Log(string.Format(Custom.Resource.ShareMailException, error), Cbi.LogLevel.Error);
			}
			finally
			{
				Subject			= string.Empty;
				ToMailAddress	= string.Empty;
			}
		}

		public async override Task OnShare(string text, string imageFilePath, object[] args)
		{
			if (args != null && args.Length > 1)
			{
				try
				{
					ToMailAddress	= args[0].ToString();
					Subject			= args[1].ToString();
				}
				catch (Exception exp)
				{
					LogAndPrint(typeof(Custom.Resource), "ShareArgsException", new[] { exp.Message }, Cbi.LogLevel.Error);
					return;
				}
			}

			await OnShare(text, imageFilePath);
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
				Name						= Custom.Resource.MailServiceName;
				Signature					= Custom.Resource.EmailSignature;
				UseOAuth					= false;

				FromMailAddress				= string.Empty;
				IsBodyHtml					= false;
				Port						= 25;
				SenderDisplayName			= string.Empty;
				Server						= string.Empty;
				Subject						= string.Empty;
				ToMailAddress				= string.Empty;
				UserName					= string.Empty;

				PreconfiguredNames			= new List<string>
				{
					Custom.Resource.ShareMailPreconfiguredManual,
					Custom.Resource.ShareMailPreconfiguredAol,
					Custom.Resource.ShareMailPreconfiguredComcast,
					Custom.Resource.ShareMailPreconfiguredGmail,
					Custom.Resource.ShareMailPreconfiguredICloud,
					Custom.Resource.ShareMailPreconfiguredOutlook,
					Custom.Resource.ShareMailPreconfiguredYahoo
				};

				SelectedPreconfiguredSetting = PreconfiguredNames[0];
			}
			else if (State == State.Terminated)
			{
				Subject			= string.Empty;
				ToMailAddress	= string.Empty;
			}
		}

		#region IPreconfiguredProvider
		public void ApplyPreconfiguredSettings(string name)
		{
			if (name == Custom.Resource.ShareMailPreconfiguredAol)
			{
				Port	= 587;
				Server	= "smtp.aol.com";
				UseSSL	= true;
			}
			else if (name == Custom.Resource.ShareMailPreconfiguredComcast)
			{
				Port	= 587;
				Server	= "smtp.comcast.net";
				UseSSL	= true;
			}
			else if (name == Custom.Resource.ShareMailPreconfiguredGmail)
			{
				Port	= 587;
				Server	= "smtp.gmail.com";
				UseSSL	= true;
			}
			else if (name == Custom.Resource.ShareMailPreconfiguredICloud)
			{
				Port	= 587;
				Server	= "smtp.mail.me.com";
				UseSSL	= true;
			}
			else if (name == Custom.Resource.ShareMailPreconfiguredOutlook)
			{
				Port	= 587;
				Server	= "smtp.live.com";
				UseSSL	= true;
			}
			else if (name == Custom.Resource.ShareMailPreconfiguredYahoo)
			{
				Port	= 587;
				Server	= "smtp.mail.yahoo.com";
				UseSSL	= true;
			}
		}

		[XmlIgnore]
		public List<string> PreconfiguredNames { get; set; }

		public string SelectedPreconfiguredSetting { get; set; }
		#endregion

		#region Properties
		[Display(ResourceType = typeof(Custom.Resource), Name = "MailServiceMailAddress", GroupName = "ShareServiceParameters", Order = 40)]
		[Required]
		public string FromMailAddress
		{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "MailServiceSenderDisplayName", GroupName = "ShareServiceParameters", Order = 45)]        //The name will show up in the text label on the Share window and the Description will be the tooltip. Order determines the order fields show up in the window.
		public string SenderDisplayName
		{ get; set; }

		[Browsable(false)]
		public bool IsBodyHtml
		{ get; set; }
	
		[Gui.Encrypt]
		[PasswordPropertyText(true)]
		[Required]
		[Display(ResourceType = typeof(Custom.Resource), Name = "ShareServicePassword", GroupName = "ShareServiceParameters", Order = 60)]
		public string Password
		{ get; set; }

		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "MailServicePort", GroupName = "ShareServiceParameters", Order = 20)]
		[Required]
		public int Port
		{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "MailServiceServer", GroupName = "ShareServiceParameters", Order = 10)]
		[Required]
		public string Server
		{ get; set; }

		[ShareField]																													//This indicates this property should show up in the Share window
		[Display(ResourceType = typeof(Custom.Resource), Name = "MailSubject", Description = "MailSubjectDescription", Order = 100)]		//The name will show up in the text label on the Share window and the Description will be the tooltip. Order determines the order fields show up in the window.
		[Browsable(false)]
		[XmlIgnore]
		public string Subject
		{ get; set; }

		[ShareField]																													//This indicates this property should show up in the Share window
		[Display(ResourceType = typeof(Custom.Resource), Name = "MailToAddress", Description = "MailToAddressDescription", Order = 0)]	//The name will show up in the text label on the Share window and the Description will be the tooltip. Order determines the order fields show up in the window.
		[Browsable(false)]
		[XmlIgnore]
		public string ToMailAddress
		{ get; set; }

		[Gui.Encrypt]
		[Display(ResourceType = typeof(Custom.Resource), Name = "ShareServiceUserName", GroupName = "ShareServiceParameters", Order = 50)]
		[Required]
		public string UserName
		{ get; set; }

		[Gui.Encrypt]
		[Display(ResourceType = typeof(Custom.Resource), Name = "MailServiceSSL", GroupName = "ShareServiceParameters", Order = 30)]
		public bool UseSSL
		{ get; set; }
		#endregion
	}
}
