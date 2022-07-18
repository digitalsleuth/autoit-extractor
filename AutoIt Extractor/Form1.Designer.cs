using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Threading;

namespace AutoIt_Extractor
{
	public partial class MainForm : Form
	{
		private static Dictionary<string, AU3_Resource> table;
		private delegate void PtrSetAttr<T>(string attr, T val, Control c);
		private delegate void PtrAppend(object val);
		private Keys keys;
		private string argFile;
		internal event EventHandler Quit;
		private List<Thread> threads;

		internal void InvokeAppend(object val)
		{
			if (lstResources.InvokeRequired)
			{
				lstResources.Invoke(new PtrAppend(InvokeAppend), val);
			}
			else
			{
				lstResources.Items.Add(val);
			}
		}

		internal void SetAttr<T>(string attr, T val, Control c)
		{
			if (c.InvokeRequired)
				c.Invoke(new PtrSetAttr<T>(SetAttr), attr, val, c);
			else
			{
				var prop = typeof(Control).GetProperty(attr);
				prop.SetValue(c, val, null);
			}
		}

		public MainForm(string[] argv)
		{
			InitializeComponent();
			argFile = null;
			if (argv.Length == 1)
			{
				argFile = argv[0];
			}
			threads = new List<Thread>();
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
			if (argFile != null)
			{
				AnalyzeFile(argFile);
			}
		}

		private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			MessageBox.Show(e.ExceptionObject.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}

		private void MainForm_DragDrop(object source, DragEventArgs e)
		{
			var files = (string[])e.Data.GetData(DataFormats.FileDrop);
			AnalyzeFile(files[0]);
		}

		private void AnalyzeFile(string path)
		{
			lstResources.Items.Clear();
			new Thread(() =>
			{
				SetAttr("Text", "", txtResCSize);
				SetAttr("Text", "", txtResDSize);
				SetAttr("Text", "", txtResTag);
				SetAttr("Text", "", txtResPath);
				SetAttr("Text", "", txtResCTime);
				SetAttr("Text", "", txtResMTime);
				SetAttr("Text", "", txtResData);

				new Thread(() => AnalyzeFileHelper(path)).Start();
			}).Start();
			lblStatus.ForeColor = System.Drawing.Color.Black;
			lblStatus.Text = "Loading ...";
		}

		private void AnalyzeFileHelper(string path)
		{
			var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
			var contents = new byte[stream.Length];
			stream.Read(contents, 0, contents.Length);
			stream.Close();
			var _buf = new byte[contents.Length];
			Array.Copy(contents, _buf, contents.Length);

			int[] pos = null;
			int xor;
			for (xor = 0; xor < 0x100; ++xor)
			{
				for (int i = 0; i < _buf.Length; ++i)
					_buf[i] = (byte)(contents[i] ^ xor);
				pos = Utils.FindAll(_buf, Utils.AU3_HEADER.Select<byte, byte>(e => (byte)(e ^ 5)).ToArray<byte>());
				if (pos != null && pos.Length > 0) break;
			}

			if (pos == null || pos.Length == 0)
			{
				SetAttr("ForeColor", System.Drawing.Color.Red, lblStatus);
				SetAttr("Text", "Script Not Found", lblStatus);
				return;
			}

			contents = _buf;

			int startPos = 0, endPos = -1;
			var possibleScripts = new Dictionary<int, int>();
			string szSubType = "";
			foreach (var entry in pos)
			{
				// Check AutoIt SubType - "AU3!EA0N" where N is a digit
				startPos = entry;
				var subtype = new byte[8];
				Array.Copy(contents, entry + 0x10, subtype, 0, 8);
				szSubType = Encoding.ASCII.GetString(subtype);
				if (!szSubType.StartsWith("AU3!"))
				{
					continue;
				}

				int stop = Utils.Find(contents, subtype, entry + 0x19);

				if (stop == -1)
				{
					/*SetAttr("ForeColor", System.Drawing.Color.Red, lblStatus);
					SetAttr("Text", "Script Not Found", lblStatus);
					return;*/
					stop = contents.Length;
				}

				endPos = stop;
				possibleScripts.Add(startPos, endPos);
			}

			bool bLegacy = false;

			if (endPos == -1 && startPos > 0)
			{
				endPos = contents.Length - 4; // -8 originally
				if (! possibleScripts.ContainsKey(startPos))
					possibleScripts.Add(startPos, endPos);
				bLegacy = true;
				szSubType = "AU3!OLD";
			}

			if (possibleScripts.Count == 0 || szSubType.Length == 0)
			{
				SetAttr("ForeColor", System.Drawing.Color.Red, lblStatus);
				SetAttr("Text", "Script Not Found", lblStatus);
				return;
			}

			SetAttr("Text", path, txtSourcePath);
			

			SetAttr("ForeColor", System.Drawing.Color.Green, lblStatus);
			SetAttr("Text", "Loaded ...", lblStatus);

			table = new Dictionary<string, AU3_Resource>();
			string status = "";
			int nValidScripts = 0;
			foreach (var e in possibleScripts)
			{
				var script = new byte[e.Value - e.Key];
				Array.Copy(contents, e.Key, script, 0, script.Length);

				try
				{
					var resp = Unpack(script, bLegacy, szSubType, out status);

					if (status != "OK")
					{
						SetAttr("ForeColor", System.Drawing.Color.Red, lblStatus);
						SetAttr("Text", "Error: " + status, lblStatus);
						return;
					}

					++nValidScripts;

					foreach (var entry in resp)
					{
						table.Add(entry.ShortTag, entry);
						InvokeAppend(entry.ShortTag);
					}
				}
				catch (Exception)
				{
					continue;
				}
			}

			if (nValidScripts == 0)
			{
				SetAttr("ForeColor", System.Drawing.Color.Red, lblStatus);
				SetAttr("Text", "Script Not Found", lblStatus);
			}
		}

		private void btnDump_Click(object sender, EventArgs e)
		{
			var entry = table[(string)lstResources.SelectedItem];
			if (entry == null)
				return;
			var saveDlg = new SaveFileDialog
			{
				Title = "Save Resource..."
			};
			if (entry.Type == Utils.TYPE.Binary)
				saveDlg.Filter = "Binary Files|*.bin;*.*";
			else
				saveDlg.Filter = "Text Files|*.txt;*.au3";
			if (saveDlg.ShowDialog() != DialogResult.OK)
				return;
			var path = saveDlg.FileName;

			var file = new FileStream(path, FileMode.Create, FileAccess.Write);
			file.Write(entry.RawData, 0, (int)entry.RawDataSize);
			file.Close();
			file.Dispose();
			lblStatus.Text = "Saved to " + path;
		}

		private void MainForm_DragEnter(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
				e.Effect = DragDropEffects.Copy;
			else
				e.Effect = DragDropEffects.None;
		}

		private void btnBrowse_Click(object sender, EventArgs e)
		{
			var openDlg = new OpenFileDialog
			{
				Filter = "AutoIt Executable|*.exe;*.a3x",
				CheckFileExists = true
			};
			if (openDlg.ShowDialog() != DialogResult.OK)
			{
				return;
			}

			AnalyzeFile(openDlg.FileName);
		}

		private delegate void PChanged(object e, EventArgs f);

		internal void UpdateStatus(object e, EventArgs f)
		{
			if (InvokeRequired)
			{
				Invoke(new PChanged(UpdateStatus), new object[] { e, f });
			}
			else
			{
				LstResources_SelectedValueChanged(e, f);
			}
		}

		private void LstResources_SelectedValueChanged(object sender, EventArgs e)
		{
			if (lstResources.SelectedItem == null)
				return;
			var item = (string)lstResources.SelectedItem;
			AU3_Resource entry = null;
			if (table.ContainsKey(item))
				entry = table[item];
			if (entry == null)
			{
				txtResCSize.Text = "";
				txtResDSize.Text = "";
				txtResTag.Text = "";
				txtResPath.Text = "";
				txtResCTime.Text = "";
				txtResMTime.Text = "";
				txtResData.Text = "";
				return;
			}

			btnDump.Enabled = entry.CompressedSize > 0;

			txtResCSize.Text = entry.CompressedSize.ToString() + " bytes";
			txtResDSize.Text = entry.DecompressedSize.ToString() + " bytes";
			txtResTag.Text = entry.Tag;
			txtResPath.Text = entry.Path;
			txtResCTime.Text = DateTime.FromFileTime((long)entry.CreationTime).ToString("ddd, MMM dd yyyy, hh:mm:ss tt");
			txtResMTime.Text = DateTime.FromFileTime((long)entry.LastWriteTime).ToString("ddd, MMM dd yyyy, hh:mm:ss tt");
			if (entry.CompressedSize > 0 && !entry.IsComplete)
			{
				txtResData.Text = "Loading ...";
			}

			/*if (! entry.IsCompressed)
			{
				entry.SourceCode = Encoding.ASCII.GetString(entry.RawData);
			}*/

			if (entry.SourceState == Utils.SOURCE_STATE.Extracted)
			{
				var newThread = new Thread(() =>
				{
					try
					{
						entry.count = 0;
						keys.Decompress(entry);
						if (entry.Tag.Contains("SCRIPT"))
						{
							entry.Tidy(this);
						}
					}
					catch (ThreadAbortException)
					{ }
				});

				threads.Add(newThread);
				newThread.Start();
			}
			if (entry.IsComplete)
			{
				SetAttr("Text", entry.Dump(), txtResData);
			}
			/*if (entry.IsCompressed)
			{
				new Thread(() => keys.Decompress(this, entry)).Start();
			}
			else
			{
				entry.SourceCode = Encoding.ASCII.GetString(entry.RawData);
			}
			if (entry.Tag.Contains("SCRIPT<"))
			{
				new Thread(() => entry.Tidy(this)).Start();
			}*/

			if (entry.State.Contains("Invalid"))
				lblStatus.ForeColor = System.Drawing.Color.Red;
			else
				lblStatus.ForeColor = System.Drawing.Color.Green;
			lblStatus.Text = entry.State;
		}

		private List<AU3_Resource> Unpack(byte[] script, bool bLegacy, string subtype, out string status)
		{
			int pos = 0x28;
			var ans = new List<AU3_Resource>();
			keys = KeyFactory.GetKeys(subtype);
			if (keys == null)
			{
				status = "Unsupported AutoIt Type - " + subtype;
				return ans;
			}
			byte[] password = null;
			bool oldAutoIt = false;
			if (bLegacy)
			{
				var passLen = BitConverter.ToInt32(script, 0x11) ^ 0xfac1;
				password = new byte[passLen];
				Array.Copy(script, 0x15, password, 0, passLen);
				keys.ShittyEncoder(password, 0xc3d2, true, oldAutoIt);
				//password = keys.DecodeString(script, 0x15, passLen, (0xc3d2 + passLen) - passLen, true, oldAutoIt);
				if (! password.All(e => Utils.PRINTABLE.Contains((char)e)))
				{
					oldAutoIt = true;
					//password = keys.DecodeString(script, 0x15, passLen, (0xc3d2 + passLen) - passLen, true, oldAutoIt);
					Array.Copy(script, 0x15, password, 0, passLen);
					keys.ShittyEncoder(password, 0xc3d2, true, oldAutoIt);
				}
				pos = 0x15 + passLen;
			}
			while (pos < script.Length)
			{
				pos += 4;   // "FILE"
				AU3_Resource res = new AU3_Resource();

				res.Update += (sender, evArgs) =>
				{
					var r = sender as AU3_Resource;
					SetAttr("ForeColor", System.Drawing.Color.Green, lblStatus);
					SetAttr("Text", r.State, lblStatus);
				};
				res.IsUnicode = keys.IsUnicode;

				if (pos >= script.Length)
					break;

				int temp = BitConverter.ToInt32(script, pos);
				temp ^= keys.TagSize;
				pos += 4;

				int len = temp;
				if (keys.IsUnicode)
					len += temp;

				if (len >= script.Length-pos)
				{
					//status = "Invalid Tag Length";
					break;
				}

				res.Tag = keys.DecodeString(script, pos, len, keys.Tag, true, oldAutoIt);

				pos += len;

				if (pos >= script.Length)
					break;

				temp = BitConverter.ToInt32(script, pos);
				temp ^= keys.PathSize;
				pos += 4;

				len = temp;
				if (keys.IsUnicode)
					len += temp;

				if (len >= script.Length-pos)
				{
					//status = "Invalid Path Length";
					break;
				}

				res.Path = keys.DecodeString(script, pos, len, keys.Path, true, oldAutoIt);
				pos += len;

				if (pos >= script.Length)
					break;

				res.IsCompressed = BitConverter.ToBoolean(script, pos);
				pos++;

				if (pos >= script.Length)
					break;

				temp = BitConverter.ToInt32(script, pos);
				temp ^= keys.CompressedSize;
				pos += 4;
				res.CompressedSize = (uint)temp;

				if (res.CompressedSize >= script.Length)
				{
					status = "Invalid Size of Compressed Resource";
					return ans;
				}

				if (pos >= script.Length)
					break;

				temp = BitConverter.ToInt32(script, pos);
				temp ^= keys.DecompressedSize;
				pos += 4;
				res.DecompressedSize = (uint)temp;

				if (! bLegacy)
				{
					temp = BitConverter.ToInt32(script, pos);
					temp ^= keys.Checksum;
					pos += 4;
					res.CheckSum = temp;
				}

				if (! oldAutoIt)
				{
					ulong time = BitConverter.ToUInt32(script, pos);
					pos += 4;
					time <<= 32;
					time |= BitConverter.ToUInt32(script, pos);
					pos += 4;
					res.CreationTime = time;

					time = BitConverter.ToUInt32(script, pos);
					pos += 4;
					time <<= 32;
					time |= BitConverter.ToUInt32(script, pos);
					pos += 4;
					res.LastWriteTime = time;
				}

				if (res.CompressedSize > 0)
				{
					// get data
					var buf = new byte[res.CompressedSize];
					Array.Copy(script, pos, buf, 0, buf.Length);
					var l = keys.Data;
					if (bLegacy)
					{
						l -= 0x849;
						foreach (var x in password)
						{
							l += (int)(sbyte)x;
						}
					}
					keys.ShittyEncoder(buf, l, false, oldAutoIt);
					res.RawData = buf;
					res.RawDataSize = res.CompressedSize;
					pos += (int)res.CompressedSize;
				}

				res.OnComplete += (o, args) =>
				{
					//SetAttr("Text", ((AU3_Resource)o).Dump(), txtResData);
					SetAttr("Text", ((AU3_Resource)o).State, lblStatus);
				};

				res.SourceState = Utils.SOURCE_STATE.Extracted;
				ans.Add(res);
			}

			status = "OK";
			return ans;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x0600000B RID: 11 RVA: 0x00002768 File Offset: 0x00000968
		private void InitializeComponent()
		{
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.label1 = new System.Windows.Forms.Label();
            this.txtSourcePath = new System.Windows.Forms.TextBox();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.lstResources = new System.Windows.Forms.ListBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.txtResTag = new System.Windows.Forms.TextBox();
            this.txtResPath = new System.Windows.Forms.TextBox();
            this.txtResCSize = new System.Windows.Forms.TextBox();
            this.txtResDSize = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.txtResCTime = new System.Windows.Forms.TextBox();
            this.txtResMTime = new System.Windows.Forms.TextBox();
            this.txtResData = new System.Windows.Forms.TextBox();
            this.btnDump = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(13, 23);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(101, 17);
            this.label1.TabIndex = 0;
            this.label1.Text = "AutoIt3 Binary:";
            // 
            // txtSourcePath
            // 
            this.txtSourcePath.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtSourcePath.Location = new System.Drawing.Point(129, 17);
            this.txtSourcePath.Margin = new System.Windows.Forms.Padding(4);
            this.txtSourcePath.Name = "txtSourcePath";
            this.txtSourcePath.ReadOnly = true;
            this.txtSourcePath.Size = new System.Drawing.Size(545, 27);
            this.txtSourcePath.TabIndex = 1;
            // 
            // btnBrowse
            // 
            this.btnBrowse.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnBrowse.Location = new System.Drawing.Point(682, 17);
            this.btnBrowse.Margin = new System.Windows.Forms.Padding(4);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(128, 28);
            this.btnBrowse.TabIndex = 2;
            this.btnBrowse.Text = "Browse...";
            this.btnBrowse.UseVisualStyleBackColor = true;
            this.btnBrowse.Click += new System.EventHandler(this.btnBrowse_Click);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.lstResources);
            this.groupBox1.Location = new System.Drawing.Point(13, 54);
            this.groupBox1.Margin = new System.Windows.Forms.Padding(4);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Padding = new System.Windows.Forms.Padding(4);
            this.groupBox1.Size = new System.Drawing.Size(308, 348);
            this.groupBox1.TabIndex = 3;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Resources";
            // 
            // lstResources
            // 
            this.lstResources.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lstResources.FormattingEnabled = true;
            this.lstResources.ItemHeight = 18;
            this.lstResources.Location = new System.Drawing.Point(13, 25);
            this.lstResources.Margin = new System.Windows.Forms.Padding(4);
            this.lstResources.Name = "lstResources";
            this.lstResources.Size = new System.Drawing.Size(280, 310);
            this.lstResources.TabIndex = 0;
            this.lstResources.SelectedIndexChanged += new System.EventHandler(this.LstResources_SelectedValueChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(11, 414);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(37, 17);
            this.label2.TabIndex = 4;
            this.label2.Text = "Tag:";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(11, 444);
            this.label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(41, 17);
            this.label3.TabIndex = 5;
            this.label3.Text = "Path:";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(11, 478);
            this.label4.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(122, 17);
            this.label4.TabIndex = 6;
            this.label4.Text = "Compressed Size:";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(11, 510);
            this.label5.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(138, 17);
            this.label5.TabIndex = 6;
            this.label5.Text = "Decompressed Size:";
            // 
            // txtResTag
            // 
            this.txtResTag.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtResTag.Location = new System.Drawing.Point(151, 410);
            this.txtResTag.Margin = new System.Windows.Forms.Padding(4);
            this.txtResTag.Name = "txtResTag";
            this.txtResTag.ReadOnly = true;
            this.txtResTag.Size = new System.Drawing.Size(659, 25);
            this.txtResTag.TabIndex = 7;
            // 
            // txtResPath
            // 
            this.txtResPath.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtResPath.Location = new System.Drawing.Point(151, 441);
            this.txtResPath.Margin = new System.Windows.Forms.Padding(4);
            this.txtResPath.Name = "txtResPath";
            this.txtResPath.ReadOnly = true;
            this.txtResPath.Size = new System.Drawing.Size(659, 25);
            this.txtResPath.TabIndex = 7;
            // 
            // txtResCSize
            // 
            this.txtResCSize.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtResCSize.Location = new System.Drawing.Point(151, 471);
            this.txtResCSize.Margin = new System.Windows.Forms.Padding(4);
            this.txtResCSize.Name = "txtResCSize";
            this.txtResCSize.ReadOnly = true;
            this.txtResCSize.Size = new System.Drawing.Size(209, 25);
            this.txtResCSize.TabIndex = 7;
            // 
            // txtResDSize
            // 
            this.txtResDSize.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtResDSize.Location = new System.Drawing.Point(151, 503);
            this.txtResDSize.Margin = new System.Windows.Forms.Padding(4);
            this.txtResDSize.Name = "txtResDSize";
            this.txtResDSize.ReadOnly = true;
            this.txtResDSize.Size = new System.Drawing.Size(209, 25);
            this.txtResDSize.TabIndex = 7;
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(380, 478);
            this.label6.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(100, 17);
            this.label6.TabIndex = 8;
            this.label6.Text = "Creation Time:";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(380, 510);
            this.label7.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(111, 17);
            this.label7.TabIndex = 8;
            this.label7.Text = "Last Write Time:";
            // 
            // txtResCTime
            // 
            this.txtResCTime.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtResCTime.Location = new System.Drawing.Point(499, 471);
            this.txtResCTime.Margin = new System.Windows.Forms.Padding(4);
            this.txtResCTime.Name = "txtResCTime";
            this.txtResCTime.ReadOnly = true;
            this.txtResCTime.Size = new System.Drawing.Size(311, 25);
            this.txtResCTime.TabIndex = 7;
            // 
            // txtResMTime
            // 
            this.txtResMTime.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtResMTime.Location = new System.Drawing.Point(499, 503);
            this.txtResMTime.Margin = new System.Windows.Forms.Padding(4);
            this.txtResMTime.Name = "txtResMTime";
            this.txtResMTime.ReadOnly = true;
            this.txtResMTime.Size = new System.Drawing.Size(311, 25);
            this.txtResMTime.TabIndex = 7;
            // 
            // txtResData
            // 
            this.txtResData.AcceptsReturn = true;
            this.txtResData.AcceptsTab = true;
            this.txtResData.Font = new System.Drawing.Font("Courier New", 10.2F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtResData.Location = new System.Drawing.Point(333, 65);
            this.txtResData.Margin = new System.Windows.Forms.Padding(4);
            this.txtResData.Multiline = true;
            this.txtResData.Name = "txtResData";
            this.txtResData.ReadOnly = true;
            this.txtResData.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtResData.Size = new System.Drawing.Size(477, 298);
            this.txtResData.TabIndex = 9;
            // 
            // btnDump
            // 
            this.btnDump.Enabled = false;
            this.btnDump.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnDump.Location = new System.Drawing.Point(333, 372);
            this.btnDump.Margin = new System.Windows.Forms.Padding(4);
            this.btnDump.Name = "btnDump";
            this.btnDump.Size = new System.Drawing.Size(148, 31);
            this.btnDump.TabIndex = 10;
            this.btnDump.Text = "Save Resource ...";
            this.btnDump.Click += new System.EventHandler(this.btnDump_Click);
            // 
            // lblStatus
            // 
            this.lblStatus.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblStatus.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblStatus.Location = new System.Drawing.Point(11, 539);
            this.lblStatus.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(799, 25);
            this.lblStatus.TabIndex = 11;
            // 
            // MainForm
            // 
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(823, 575);
            this.Controls.Add(this.txtResMTime);
            this.Controls.Add(this.txtResCTime);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.txtResDSize);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.txtResCSize);
            this.Controls.Add(this.txtResPath);
            this.Controls.Add(this.txtResTag);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.btnBrowse);
            this.Controls.Add(this.txtSourcePath);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.txtResData);
            this.Controls.Add(this.btnDump);
            this.Controls.Add(this.lblStatus);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "AutoIt Extractor";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.MainForm_DragDrop);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.MainForm_DragEnter);
            this.groupBox1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

		}


		// Token: 0x04000002 RID: 2
		private readonly System.ComponentModel.IContainer components;

		// Token: 0x04000003 RID: 3
		private Label label1;

		// Token: 0x04000004 RID: 4
		private TextBox txtSourcePath;

		// Token: 0x04000005 RID: 5
		private Button btnBrowse;

		// Token: 0x04000006 RID: 6
		private GroupBox groupBox1;

		// Token: 0x04000007 RID: 7
		private ListBox lstResources;

		// Token: 0x04000008 RID: 8
		private Label label2;

		// Token: 0x04000009 RID: 9
		private Label label3;

		// Token: 0x0400000A RID: 10
		private Label label4;

		// Token: 0x0400000B RID: 11
		private Label label5;

		// Token: 0x0400000C RID: 12
		private TextBox txtResTag;

		// Token: 0x0400000D RID: 13
		private TextBox txtResPath;

		// Token: 0x0400000E RID: 14
		private TextBox txtResCSize;

		// Token: 0x0400000F RID: 15
		private TextBox txtResDSize;

		// Token: 0x04000010 RID: 16
		private Label label6;

		// Token: 0x04000011 RID: 17
		private Label label7;

		// Token: 0x04000012 RID: 18
		private TextBox txtResCTime;

		// Token: 0x04000013 RID: 19
		private TextBox txtResMTime;

		// Token: 0x04000014 RID: 20
		internal TextBox txtResData;

		// Token: 0x04000015 RID: 21
		private Button btnDump;

		// Token: 0x04000016 RID: 22
		internal Label lblStatus;
	}
}
