﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;
using epg123.Properties;

namespace epg123
{
    public partial class frmMain : Form
    {
        private enum LineupColumn
        {
            CallSign = 0,
            Channel = 1,
            StationID = 2,
            Name = 3
        };
        private List<string> lineups;
        private epgTaskScheduler task = new epgTaskScheduler();
        private HashSet<string> newLineups = new HashSet<string>();
        private ImageList imageList = new ImageList();
        private string grabberVersion
        {
            get
            {
                string[] temp = Helper.epg123Version.Split('.');
                return (string.Format("{0}.{1}.{2}", temp[0], temp[1], temp[2]));
            }
        }
        private bool newLogin = true;
        public bool restartAsAdmin = false;

        public epgConfig config = new epgConfig();
        private epgConfig oldConfig = new epgConfig();
        public bool Execute = false;
        public bool import = false;
        public bool match = false;
        private double dpiScaleFactor = 1.0;

        public frmMain()
        {
            // required to show UAC shield on buttons
            Application.EnableVisualStyles();

            // create form objects
            InitializeComponent();

            // adjust components for screen dpi
            using (Graphics g = CreateGraphics())
            {
                if ((g.DpiX != 96) || (g.DpiY != 96))
                {
                    dpiScaleFactor = g.DpiX / 96;

                    // adjust image size for list view items
                    imageList.ImageSize = new Size((int)(g.DpiX / 6), (int)(g.DpiY / 6));

                    // adjust column widths for list views
                    ListView[] listviews = { lvL1Lineup, lvL2Lineup, lvL3Lineup, lvL4Lineup };
                    foreach (ListView listview in listviews)
                    {
                        foreach (ColumnHeader column in listview.Columns)
                        {
                            column.Width = (int)(column.Width * dpiScaleFactor);
                        }
                    }
                }
            }

            toolStrip1.ImageScalingSize = toolStrip2.ImageScalingSize = toolStrip3.ImageScalingSize = toolStrip4.ImageScalingSize = toolStrip5.ImageScalingSize = new Size((int)(dpiScaleFactor * 16), (int)(dpiScaleFactor * 16));
        }
        private void ConfigForm_Load(object sender, EventArgs e)
        {
            // copy over window size and location from previous version if needed
            if (Settings.Default.UpgradeRequired)
            {
                Settings.Default.Upgrade();
                Settings.Default.UpgradeRequired = false;
                Settings.Default.Save();
            }

            // initialize the schedules direct api
            sdAPI.Initialize("EPG123", grabberVersion);

            // complete the title bar label with version number
            this.Text += " v" + grabberVersion;

            // check for updates
            sdClientVersionResponse veresp = sdAPI.sdCheckVersion();
            if ((veresp != null) && (veresp.Version != grabberVersion))
            {
                lblUpdate.Text = string.Format("UPDATE AVAILABLE (v{0})", veresp.Version);
            }

            // set registry and check if registered for event log
            createEventLogSource();

            // set imagelist for listviews
            lvL1Lineup.SmallImageList = lvL1Lineup.LargeImageList = imageList;
            lvL2Lineup.SmallImageList = lvL2Lineup.LargeImageList = imageList;
            lvL3Lineup.SmallImageList = lvL3Lineup.LargeImageList = imageList;
            lvL4Lineup.SmallImageList = lvL4Lineup.LargeImageList = imageList;
            lvL5Lineup.SmallImageList = lvL5Lineup.LargeImageList = imageList;

            // set the splitter distance
            splitContainer1.Panel1MinSize = (int)(splitContainer1.Panel1MinSize * dpiScaleFactor);

            // restore window position and size
            if ((Settings.Default.WindowLocation != null) && (Settings.Default.WindowLocation != new Point(-1, -1)))
            {
                Location = Settings.Default.WindowLocation;
            }
            if (Settings.Default.WindowSize != null)
            {
                Size = Settings.Default.WindowSize;
            }
            if (Settings.Default.WindowMaximized)
            {
                WindowState = FormWindowState.Maximized;
            }
        }
        private void frmMain_Shown(object sender, EventArgs e)
        {
            // get login/password info from configuration file if exists
            if (!File.Exists(Helper.Epg123CfgPath)) return;

            // login to Schedules Direct and get a token
            try
            {
                using (StreamReader stream = new StreamReader(Helper.Epg123CfgPath, Encoding.Default))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(epgConfig));
                    TextReader reader = new StringReader(stream.ReadToEnd());
                    config = (epgConfig)serializer.Deserialize(reader);
                    reader.Close();

                    oldConfig = config.Clone();
                }

                if (!string.IsNullOrEmpty(config.UserAccount.LoginName) && !string.IsNullOrEmpty(config.UserAccount.PasswordHash))
                {
                    txtLoginName.Text = config.UserAccount.LoginName;
                    txtPassword.Text = "********";

                    this.Refresh();
                    btnLogin_Click(null, null);
                }
            }
            catch (Exception ex)
            {
                Logger.WriteError(ex.Message);
            }

            // if client was started as elevated to perform an action
            if (Helper.UserHasElevatedRights && File.Exists(Helper.EButtonPath))
            {
                using (StreamReader sr = new StreamReader(Helper.EButtonPath))
                {
                    string line = sr.ReadLine();
                    if (line.Contains("createTask") || line.Contains("deleteTask"))
                    {
                        btnTask_Click(null, null);
                        tabConfigs.SelectedTab = tabTask;
                    }
                    sr.Close();
                }
                File.Delete(Helper.EButtonPath);
            }
            this.Cursor = Cursors.Arrow;
        }

        private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            // give option to save if there were changes
            RefreshConfiguration();
            if (!btnLogin.Enabled && !config.Equals(oldConfig) && DialogResult.Yes == MessageBox.Show("There have been changes made to your configuration. Do you wish to save changes before exiting?", "Configuration Change", MessageBoxButtons.YesNo, MessageBoxIcon.Question))
            {
                btnSave_Click(sender, null);
            }

            // save the windows size and location
            if (WindowState == FormWindowState.Normal)
            {
                Settings.Default.WindowLocation = Location;
                Settings.Default.WindowSize = Size;
            }
            else
            {
                Settings.Default.WindowLocation = RestoreBounds.Location;
                Settings.Default.WindowSize = RestoreBounds.Size;
            }
            Settings.Default.WindowMaximized = (WindowState == FormWindowState.Maximized);
            Settings.Default.Save();
        }

        #region ========== Elevated Rights, Registry, and Event Log =========
        private void elevateRights()
        {
            // save current settings
            if (!string.IsNullOrEmpty(txtAcctExpires.Text))
            {
                btnSave_Click(null, null);
            }

            // start a new process with elevated rights
            restartAsAdmin = true;
            Application.Exit();
        }
        private bool createEventLogSource()
        {
            RegistryKey key = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\services\\eventlog\\Media Center\\EPG123");
            if (key == null)
            {
                try
                {
                    if (!EventLog.SourceExists("EPG123"))
                    {
                        EventSourceCreationData sourceData = new EventSourceCreationData("EPG123", "Media Center");
                        EventLog.CreateEventSource(sourceData);
                    }
                    if (!EventLog.SourceExists("EPG123Client"))
                    {
                        EventSourceCreationData sourceData = new EventSourceCreationData("EPG123Client", "Media Center");
                        EventLog.CreateEventSource(sourceData);
                    }
                }
                catch
                {
                    MessageBox.Show("EPG123 has not been registered as a source for Media Center event logs. This GUI must be executed with elevated rights to add EPG123 as a valid source.",
                                    "Event Log Permissions Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return false;
                }
            }
            return true;
        }
        #endregion

        #region ========== Scheduled Task ==========
        private void updateTaskPanel(bool silent = false)
        {
            // get status
            task.queryTask(silent);

            // set task create/delete button text
            btnTask.Text = (task.exist || task.existNoAccess) ? "Delete" : "Create";

            // update scheduled task run time
            tbSchedTime.Enabled = (!task.exist && !task.existNoAccess);
            tbSchedTime.Text = task.schedTime.ToString("HH:mm");
            lblUpdateTime.Enabled = (!task.exist && !task.existNoAccess);

            // set sheduled task wake checkbox
            cbTaskWake.Enabled = (!task.exist && !task.existNoAccess);
            cbTaskWake.Checked = task.wake;

            // determine which action is the client action
            int clientIndex = -1;
            int epg123Index = -1;
            if (task.exist)
            {
                for (int i = 0; i < task.actions.Length; ++i)
                {
                    if (task.actions[i].Path.ToLower().Contains("epg123.exe")) epg123Index = i;
                    if (task.actions[i].Path.ToLower().Contains("epg123client.exe")) clientIndex = i;
                }

                // verify task configuration with respect to this executable
                if (!silent && epg123Index >= 0 && !task.actions[epg123Index].Path.ToLower().Replace("\"", "").Equals(Helper.Epg123ExePath.ToLower()))
                {
                    MessageBox.Show(string.Format("The location of this program file is not the same location configured in the Scheduled Task.\n\nThis program:\n{0}\n\nTask program:\n{1}",
                                                  Helper.Epg123ExePath, task.actions[epg123Index].Path), "Configuration Warning", MessageBoxButtons.OK);
                }
            }

            // set import and automatch checkbox states
            if (!File.Exists(Helper.Epg123ClientExePath) || !File.Exists(Helper.EhshellExeFilePath))
            {
                cbImport.Enabled = cbAutomatch.Enabled = false;
                cbImport.Checked = cbAutomatch.Checked = false;
            }
            else
            {
                cbImport.Enabled = !task.exist && !task.existNoAccess;
                cbImport.Checked = (clientIndex >= 0) || (!task.exist && config.AutoImport);
                cbAutomatch.Enabled = !task.exist && !task.existNoAccess && cbImport.Checked;
                cbAutomatch.Checked = ((clientIndex >= 0) && task.actions[clientIndex].Arguments.ToLower().Contains("-match")) || (!task.exist && config.Automatch);
            }

            // update status string
            if (task.exist && (epg123Index >= 0))
            {
                lblSchedStatus.Text = task.statusString;
                lblSchedStatus.ForeColor = Color.Black;
            }
            else if (task.exist && (clientIndex >= 0))
            {
                lblSchedStatus.Text = "### Client Mode ONLY - Guide will not be downloaded. ###";
                lblSchedStatus.ForeColor = Color.Red;
            }
            else
            {
                lblSchedStatus.Text = task.statusString;
                lblSchedStatus.ForeColor = Color.Red;
            }
        }
        private void btnTask_Click(object sender, EventArgs e)
        {
            if (sender != null) // null sender means we restarted to finish in administrator mode
            {
                // create new task if file location is valid
                if (!task.exist)
                {
                    // create task using epg123.exe & epg123Client.exe
                    if (cbImport.Checked)
                    {
                        epgTaskScheduler.TaskActions[] actions = new epgTaskScheduler.TaskActions[2];
                        actions[0].Path = Helper.Epg123ExePath;
                        actions[0].Arguments = "-update";
                        actions[1].Path = Helper.Epg123ClientExePath;
                        actions[1].Arguments = "-i \"" + Helper.Epg123MxfPath + "\"" + ((cbAutomatch.Checked) ? " -match" : null);
                        task.createTask(cbTaskWake.Checked, tbSchedTime.Text, actions);
                    }
                    // create task using epg123.exe
                    else
                    {
                        epgTaskScheduler.TaskActions[] actions = new epgTaskScheduler.TaskActions[1];
                        actions[0].Path = Helper.Epg123ExePath;
                        actions[0].Arguments = "-update";
                        task.createTask(cbTaskWake.Checked, tbSchedTime.Text, actions);
                    }
                    btnSave_Click(null, null);
                }
            }

            // check for elevated rights and open new process if necessary
            if (!Helper.UserHasElevatedRights)
            {
                if (task.exist || task.existNoAccess)
                {
                    Helper.WriteEButtonFile("deleteTask");
                }
                else
                {
                    Helper.WriteEButtonFile("createTask");
                }
                elevateRights();
                return;
            }
            else if (task.exist)
            {
                task.deleteTask();
            }
            else
            {
                task.importTask();
            }

            // update panel with current information
            updateTaskPanel();
        }
        #endregion

        #region ========== Login ==========
        private void txtLogin_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)13)
            {
                btnLogin_Click(null, null);
            }
        }
        private void btnLogin_Click(object sender, EventArgs e)
        {
            // disable input fields while trying to login
            this.Cursor = Cursors.WaitCursor;
            txtLoginName.Enabled = txtPassword.Enabled = btnLogin.Enabled = false;

            if ((config.UserAccount != null) && !string.IsNullOrEmpty(config.UserAccount.LoginName) && !string.IsNullOrEmpty(config.UserAccount.PasswordHash))
            {
                // use the new username/password combination, otherwise use the stored username/password
                if (!txtLoginName.Text.ToLower().Equals(config.UserAccount.LoginName.ToLower()) || !txtPassword.Text.Equals("********"))
                {
                    config.UserAccount = new SdUserAccount()
                    {
                        LoginName = txtLoginName.Text,
                        Password = txtPassword.Text
                    };
                }
                else
                {
                    newLogin = false;
                }
            }
            else if (!string.IsNullOrEmpty(txtLoginName.Text) && !string.IsNullOrEmpty(txtPassword.Text))
            {
                config.UserAccount = new SdUserAccount()
                {
                    LoginName = txtLoginName.Text,
                    Password = txtPassword.Text
                };
            }
            else
            {
                config.UserAccount = new SdUserAccount()
                {
                    LoginName = string.Empty,
                    Password = string.Empty
                };
            }

            txtLoginName.Enabled = txtPassword.Enabled = btnLogin.Enabled = !loginUser();
            this.Cursor = Cursors.Arrow;
        }
        private bool loginUser()
        {
            bool ret;
            string errorString = string.Empty;
            if (ret = sdAPI.sdGetToken(config.UserAccount.LoginName, config.UserAccount.PasswordHash, ref errorString))
            {
                // get membership expiration
                getUserStatus();

                // create 2 lists : stations to be downloaded and stations not to be downloaded
                // stations that do not exist in either list are NEW
                if (config.StationID != null)
                {
                    populateIncludedExcludedStations(config.StationID);
                }

                // populate the listviews with lineup channels
                buildLineupTabs();

                // set configuration options
                if (config.DaysToDownload <= 0)
                {
                    numDays.Value = 14;
                    cbTVDB.Checked = true;
                    cbOadOverride.Checked = true;
                    cbTMDb.Checked = true;
                    cbSdLogos.Checked = true;
                    cbAddNewStations.Checked = true;
                }
                else
                {
                    numDays.Value = Math.Min(config.DaysToDownload, numDays.Maximum);
                    cbPrefixTitle.Checked = config.PrefixEpisodeTitle;
                    cbAppendDescription.Checked = config.AppendEpisodeDesc;
                    cbOadOverride.Checked = config.OADOverride;
                    cbTMDb.Checked = config.TMDbCoverArt;
                    cbSdLogos.Checked = config.IncludeSDLogos;
                    
                    cbTVDB.Checked = config.TheTVDBNumbers;
                    cbPrefixDescription.Checked = config.PrefixEpisodeDescription;
                    cbAlternateSEFormat.Checked = config.AlternateSEFormat;
                    cbAddNewStations.Checked = config.AutoAddNew;
                    cbSeriesPosterArt.Checked = config.SeriesPosterArt;
                    cbModernMedia.Checked = config.ModernMediaUiPlusSupport;
                    cmbPreferredLogos.SelectedIndex = (int)(Helper.PreferredLogos)Enum.Parse(typeof(Helper.PreferredLogos), config.PreferredLogoStyle, true);
                    cmbAlternateLogos.SelectedIndex = (int)(Helper.PreferredLogos)Enum.Parse(typeof(Helper.PreferredLogos), config.AlternateLogoStyle, true);
                    ckChannelNumbers.Checked = config.XmltvIncludeChannelNumbers;
                    ckChannelLogos.Checked = !string.IsNullOrEmpty(config.XmltvIncludeChannelLogos) && (config.XmltvIncludeChannelLogos != "false");
                    ckLocalLogos.Checked = (config.XmltvIncludeChannelLogos == "local") || (config.XmltvIncludeChannelLogos == "substitute");
                    ckUrlLogos.Checked = (config.XmltvIncludeChannelLogos == "url");
                    ckSubstitutePath.Checked = (config.XmltvIncludeChannelLogos == "substitute");
                    txtSubstitutePath.Text = config.XmltvLogoSubstitutePath;
                    ckXmltvFillerData.Checked = config.XmltvAddFillerData;
                    ckXmltvExtendedInfo.Checked = config.XmltvExtendedInfoInTitleDescriptions;
                    numFillerDuration.Value = config.XmltvFillerProgramLength;
                    rtbFillerDescription.Text = config.XmltvFillerProgramDescription;
                    tbXmltvOutput.Text = config.XmltvOutputFile ?? Helper.Epg123XmltvPath;
                    cbBrandLogo.Checked = !config.BrandLogoImage.Equals("none");

                    cbXmltv.Checked = config.CreateXmltv;
                }

                // get persistent cfg values
                if (!task.exist && !task.existNoAccess && File.Exists(Helper.Epg123ClientExePath))
                {
                    if (File.Exists(Helper.Epg123CfgPath))
                    {
                        cbImport.Checked = cbAutomatch.Enabled = config.AutoImport;
                        cbAutomatch.Checked = config.Automatch;
                    }
                    else
                    {
                        cbImport.Checked = cbAutomatch.Enabled = true;
                        cbAutomatch.Checked = true;
                    }
                }

                // enable form controls
                tabLineups.Enabled = true;
                tabConfigs.Enabled = true;
                btnSave.Enabled = true;
                btnExecute.Enabled = true;
                btnClientLineups.Enabled = true;

                // update the task panel
                updateTaskPanel();

                // automatically save a .cfg file with account info if first login or password change
                if (newLogin)
                {
                    btnSave_Click(null, null);
                }
            }
            else
            {
                MessageBox.Show(errorString, "Login Failed");
            }

            return ret;
        }
        private void getUserStatus()
        {
            sdUserStatusResponse status = sdAPI.sdGetStatus();
            if (status == null)
            {
                txtAcctExpires.Text = "Unknown";
            }
            else
            {
                txtAcctExpires.Text = status.Account.Expires.ToLocalTime().ToString();
                if (status.Account.Expires - DateTime.Now < TimeSpan.FromDays(14.0))
                {
                    // weird fact: the text color of a read-only textbox will only change after you set the backcolor
                    txtAcctExpires.ForeColor = Color.Red;
                    txtAcctExpires.BackColor = txtAcctExpires.BackColor;
                }
                if ((status.Lineups == null) || (status.Lineups.Count == 0))
                {
                    MessageBox.Show("There are no lineups in your SD-JSON account. You must\nadd at least one lineup to proceed.", "No Lineups in Account", MessageBoxButtons.OK);
                    btnClientConfig_Click(null, null);
                }
            }
        }
        #endregion

        #region ========== Setup Lineup ListViews and Tabs ==========
        private void buildLineupTabs()
        {
            ListView[] listViews = { lvL1Lineup, lvL2Lineup, lvL3Lineup, lvL4Lineup, lvL5Lineup };
            ToolStripLabel[] listViewLabels = { lblL1Lineup, lblL2Lineup, lblL3Lineup, lblL4Lineup };
            ToolStrip[] toolStrips = { toolStrip1, toolStrip2, toolStrip3, toolStrip4, toolStrip5 };

            // focus on first tab
            tabLineups.SelectedIndex = 0;

            // temporarily disable item check
            disable_itemCheck = true;

            // clear lineup listviews and title
            for (int i = 0; i < listViews.Length; ++i)
            {
                listViews[i].Items.Clear();
                listViews[i].ListViewItemSorter = null;
                if (i < 4) listViewLabels[i].Text = string.Empty;
                toolStrips[i].Enabled = false;
                ExcludeLineup(i);
            }

            // populate the listviews with channels/services
            buildListViewChannels();
            buildCustomListViewChannels();

            // assign a listviewcolumnsorter to a listview
            assignColumnSorters();

            // re-enable item check
            disable_itemCheck = false;
        }
        private void buildCustomListViewChannels()
        {
            btnCustomLineup.DropDownItems.Clear();
            if (File.Exists(Helper.Epg123CustomLineupsXmlPath))
            {
                CustomLineups customLineups = new CustomLineups();
                using (StreamReader stream = new StreamReader(Helper.Epg123CustomLineupsXmlPath, Encoding.Default))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(CustomLineups));
                    TextReader reader = new StringReader(stream.ReadToEnd());
                    customLineups = (CustomLineups)serializer.Deserialize(reader);
                    reader.Close();
                }

                foreach (CustomLineup lineup in customLineups.CustomLineup)
                {
                    btnCustomLineup.DropDownItems.Add(string.Format("{0} ({1})", lineup.Name, lineup.Location)).Tag = lineup;
                }
                toolStrip5.Enabled = true;
            }
            else
            {
                btnCustomLineup.Text = "Click here to manage custom lineups.";
                btnCustomLineup.Tag = string.Empty;
                toolStrip5.Enabled = false;
            }

            if (btnCustomLineup.DropDownItems.Count > 0)
            {
                if (!newLogin)
                {
                    foreach (ToolStripItem item in btnCustomLineup.DropDownItems)
                    {
                        if (config.IncludedLineup.Contains((item.Tag as CustomLineup).Lineup as string))
                        {
                            item.PerformClick();
                            L5includeToolStripMenuItem.PerformClick();
                            return;
                        }
                    }
                }
                btnCustomLineup.DropDownItems[0].PerformClick();
            }
        }
        private CustomStation PrimaryOrAlternateStation(CustomStation station)
        {
            CustomStation ret = new CustomStation()
            {
                Alternate = station.Alternate,
                Callsign = station.Callsign,
                Name = station.Name,
                Number = station.Number,
                StationId = station.StationId,
                Subnumber = station.Subnumber
            };

            ListView[] listviews = { lvL1Lineup, lvL2Lineup, lvL3Lineup, lvL4Lineup };
            foreach (ListView listView in listviews)
            {
                if (listView.Items != null)
                {
                    foreach (ListViewItem item in listView.Items)
                    {
                        if (item.SubItems[(int)LineupColumn.StationID].Text.Equals(station.StationId))
                        {
                            return station;
                        }
                        else if (item.SubItems[(int)LineupColumn.StationID].Text.Equals(station.Alternate))
                        {
                            ret.Callsign = item.SubItems[(int)LineupColumn.CallSign].Text;
                            ret.Name = item.SubItems[(int)LineupColumn.Name].Text;
                            ret.StationId = item.SubItems[(int)LineupColumn.StationID].Text;
                        }
                    }
                }
            }
            return ret;
        }
        private void btnCustomLineup_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            CustomLineup lineup = e.ClickedItem.Tag as CustomLineup;
            btnCustomLineup.Text = e.ClickedItem.Text;
            btnCustomLineup.Tag = lineup.Lineup;

            if (lvL5Lineup.Items.Count > 0)
            {
                lvL5Lineup.Items.Clear();
            }

            List<ListViewItem> items = new List<ListViewItem>();
            foreach (CustomStation station in lineup.Station)
            {
                CustomStation stationItem = PrimaryOrAlternateStation(station);

                string channel = stationItem.Number.ToString();
                channel += (stationItem.Subnumber != 0) ? "." + stationItem.Subnumber.ToString() : string.Empty;
                items.Add(new ListViewItem(
                    new string[]
                    {
                        stationItem.Callsign,
                        channel,
                        stationItem.StationId,
                        stationItem.Name
                    })
                {
                    Checked = allStationIDs.Contains(station.StationId) || allStationIDs.Contains(station.Alternate),
                    ForeColor = allStationIDs.Contains(stationItem.StationId) ? SystemColors.WindowText : SystemColors.GrayText
                });
            }

            lockCustomCheckboxes = false;
            lvL5Lineup.Items.AddRange(items.ToArray());
            lockCustomCheckboxes = true;
        }

        bool lockCustomCheckboxes = false;
        HashSet<string> allStationIDs;
        private void buildListViewChannels()
        {
            allStationIDs = new HashSet<string>();
            lineups = new List<string>();
            sdlogos = new Dictionary<string, string>();
            ListView[] listViews = { lvL1Lineup, lvL2Lineup, lvL3Lineup, lvL4Lineup };
            ToolStripLabel[] listViewLabels = { lblL1Lineup, lblL2Lineup, lblL3Lineup, lblL4Lineup };
            ToolStrip[] toolStrips = { toolStrip1, toolStrip2, toolStrip3, toolStrip4 };

            // retrieve lineups from SD
            SdLineupResponse clientLineups = sdAPI.sdGetLineups();
            if (clientLineups == null) return;

            // build listviews with lineups and channels
            int processedLineup = 0;
            foreach (SdLineup clientLineup in clientLineups.Lineups)
            {
                // initialize an array of listviewitems
                List<ListViewItem> listViewItems = new List<ListViewItem>();

                // record lineup unique id
                lineups.Add(clientLineup.Lineup);

                // process the lineup map
                if (!clientLineup.IsDeleted)
                {
                    // set the include globe state with checkmark and update label
                    if (((config.IncludedLineup != null) && config.IncludedLineup.Contains(clientLineup.Lineup)) || newLineups.Contains(clientLineup.Lineup)) IncludeLineup(processedLineup);
                    if (processedLineup < listViews.Length) listViewLabels[processedLineup].Text = clientLineup.Name + " (" + clientLineup.Location + ")";

                    // request the lineup's station maps
                    SdStationMapResponse lineupMap = sdAPI.sdGetStationMaps(clientLineup.Lineup);
                    if (lineupMap == null) continue;

                    // match station with mapping for lineup number and subnumbers
                    foreach (SdLineupStation station in lineupMap.Stations)
                    {
                        if (station == null) continue;
                        string stationLanguage = string.Empty;
                        if (station.BroadcastLanguage != null)
                        {
                            stationLanguage = station.BroadcastLanguage[0];
                            if (station.BroadcastLanguage.Length > 1)
                            {
                                foreach (string lang in station.BroadcastLanguage)
                                {
                                    if (!lang.Equals("en", StringComparison.OrdinalIgnoreCase))
                                    {
                                        stationLanguage = lang;
                                        break;
                                    }
                                }
                            }
                        }

                        // use hashset to make sure we don't duplicate channel entries
                        HashSet<string> channelNumbers = new HashSet<string>();
                        allStationIDs.Add(station.StationID);

                        foreach (SdLineupMap map in lineupMap.Map)
                        {
                            int number = -1;
                            int subnumber = 0;
                            if (map.StationID.Equals(station.StationID))
                            {
                                // create what will be the ListViewItem Tag
                                SdChannelDownload dlStation = new SdChannelDownload()
                                {
                                    CallSign = station.Callsign,
                                    StationID = station.StationID,
                                    customCallSign = checkCustomCallsign(station.StationID),
                                    customServiceName = checkCustomServicename(station.StationID),
                                    HDOverride = checkHdOverride(station.StationID),
                                    SDOverride = checkSdOverride(station.StationID)
                                };

                                // QAM
                                if (map.ChannelMajor > 0)
                                {
                                    number = map.ChannelMajor;
                                    subnumber = map.ChannelMinor;
                                }

                                // ATSC or NTSC
                                else if (map.AtscMajor > 0)
                                {
                                    number = map.AtscMajor;
                                    subnumber = map.AtscMinor;
                                }
                                else if (map.UhfVhf > 0)
                                {
                                    number = map.UhfVhf;
                                }

                                // Cable or Satellite
                                else if (!string.IsNullOrEmpty(map.Channel))
                                {
                                    //subnumber = 0;
                                    if (Regex.Match(map.Channel, @"[A-Za-z]{1}[\d]{4}").Length > 0)
                                    {
                                        // 4dtv has channels starting with 2 character satellite identifier
                                        number = int.Parse(map.Channel.Substring(2));
                                    }
                                    else if (!int.TryParse(Regex.Replace(map.Channel, "[^0-9.]", ""), out number))
                                    {
                                        // if channel number is not a whole number, must be a decimal number
                                        string[] numbers = Regex.Replace(map.Channel, "[^0-9.]", "").Replace('_', '.').Replace("-", ".").Split('.');
                                        if (numbers.Length == 2)
                                        {
                                            number = int.Parse(numbers[0]);
                                            subnumber = int.Parse(numbers[1]);
                                        }
                                    }
                                }

                                string channelNumber = number.ToString() + ((subnumber > 0) ? "." + subnumber.ToString() : null);
                                if (channelNumbers.Add(channelNumber + ":" + station.StationID))
                                {
                                    listViewItems.Add(addListviewChannel(dlStation.customCallSign ?? station.Callsign, channelNumber, station.StationID, dlStation.customServiceName ?? station.Name, dlStation, stationLanguage));

                                    // URIs to channel logos are here ... store them for use if needed
                                    string dummy;
                                    if ((station.StationLogos != null) && station.StationLogos.Count > 0)
                                    {
                                        for (int i = 0; i < station.StationLogos.Count; ++i)
                                        {
                                            if (!sdlogos.TryGetValue(station.Callsign + "-" + (i + 1).ToString(), out dummy))
                                            {
                                                sdlogos.Add(station.Callsign + "-" + (i + 1).ToString(), station.StationLogos[i].URL);
                                            }
                                        }
                                    }
                                    else if ((station.Logo != null) && !sdlogos.TryGetValue(station.Callsign, out dummy))
                                    {
                                        sdlogos.Add(station.Callsign, station.Logo.URL);
                                    }
                                }
                            }
                        }
                    }

                    // add all the listview items to the listview
                    if (listViewItems.Count > 0 && processedLineup < listViews.Length)
                    {
                        listViews[processedLineup].Items.AddRange(listViewItems.ToArray());
                        toolStrips[processedLineup].Enabled = true;
                    }
                }
                else if (processedLineup < listViews.Length)
                {
                    listViewLabels[processedLineup].Text = "[Deleted] (" + clientLineup.Lineup + ")";
                }
                ++processedLineup;
            }
        }
        private ListViewItem addListviewChannel(string callsign, string number, string stationid, string name, SdChannelDownload dlstation, string language)
        {
            bool channelIsNew = !includedStations.Contains(stationid) && !excludedStations.Contains(stationid) && (includedStations.Count + excludedStations.Count > 0);
            return new ListViewItem(
                new string[]
                {
                    callsign,
                    number,
                    stationid,
                    name
                })
            {
                Tag = dlstation,
                Checked = includedStations.Contains(stationid) || (config.AutoAddNew && !excludedStations.Contains(stationid) && !btnClientLineups.Enabled),
                ImageKey = getLanguageIcon(language),
                BackColor = channelIsNew ? Color.Pink : default(Color)
            };
        }
        private string getLanguageIcon(string language)
        {
            if (string.IsNullOrEmpty(language)) language = "zz";

            language = language.ToLower().Substring(0, 2);
            if (imageList.Images.Keys.Contains(language))
            {
                return language;
            }

            imageList.Images.Add(language, drawText(language, new Font(lvL1Lineup.Font.Name, 16, FontStyle.Bold, lvL1Lineup.Font.Unit)));
            return language;
        }
        private Image drawText(string text, Font font)
        {
            byte[] textBytes;
            try
            {
                textBytes = ASCIIEncoding.ASCII.GetBytes(new CultureInfo(text).ThreeLetterISOLanguageName);
            }
            catch
            {
                textBytes = ASCIIEncoding.ASCII.GetBytes("zaa");
                //Logger.WriteError(string.Format("{0} not supported.", text));
            }

            // establish backColor based on language identifier
            int bitWeight = 8;
            int colorBase = 0x7A - 0xFF / bitWeight;
            if (textBytes.Length < 3) { Array.Resize(ref textBytes, 3); textBytes[2] = textBytes[0]; }
            Color backColor = Color.FromArgb((textBytes[0] - colorBase) * bitWeight,
                                             (textBytes[1] - colorBase) * bitWeight,
                                             (textBytes[2] - colorBase) * bitWeight);

            // determine best textColor
            int threshold = 140;
            int brightness = (int)Math.Sqrt(backColor.R * backColor.R * 0.299 +
                                            backColor.G * backColor.G * 0.587 +
                                            backColor.B * backColor.B * 0.114);
            Color textColor = (brightness < threshold) ? Color.White : Color.Black;

            // determine size of text with font
            SizeF textSize;
            using (Image img = new Bitmap(1, 1))
            {
                using (Graphics g = Graphics.FromImage(img))
                {
                    textSize = g.MeasureString(text, font);
                }
            }

            // create the text image in a box
            Image image = new Bitmap((int)textSize.Width + 1, (int)textSize.Height + 1);
            using (Graphics g = Graphics.FromImage(image))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                // paint the background
                g.Clear(backColor);

                // draw a box around the border
                g.DrawRectangle(Pens.Black, 0, 0, image.Width - 2, image.Height - 2);

                // draw the text in the box
                using (Brush textBrush = new SolidBrush(textColor))
                {
                    g.DrawString(text, font, textBrush, 0, 0);
                }
            }

            return image;
        }
        #endregion

        #region ========== ListView Tab Widgets ==========
        bool disable_itemCheck = false;
        private void btnAll_Click(object sender, EventArgs e)
        {
            ToolStripButton[] btn = { btnL1All, btnL2All, btnL3All, btnL4All };
            ListView[] lv = { lvL1Lineup, lvL2Lineup, lvL3Lineup, lvL4Lineup };

            // determine which lineup the button click is for
            for (int i = 0; i < btn.Length; ++i)
            {
                if (btn[i].Equals(sender))
                {
                    // escape if the listview is not enabled
                    if (!lv[i].Enabled) break;

                    disable_itemCheck = true;
                    HashSet<string> addStations = new HashSet<string>();

                    // scan through lineup and check any unchecked items
                    foreach (ListViewItem item in lv[i].Items)
                    {
                        if (!item.Checked)
                        {
                            string stationId = item.SubItems[2].Text;
                            addStations.Add(stationId);
                            includedStations.Add(stationId);
                            excludedStations.Remove(stationId);
                            item.Checked = true;
                        }
                    }

                    // if there were no items to check, stop
                    if (addStations.Count == 0) break;

                    // scan through all lineups and check the affected stations
                    foreach (ListView listview in lv)
                    {
                        foreach (ListViewItem item in listview.Items)
                        {
                            if (!item.Checked && addStations.Contains(item.SubItems[2].Text)) item.Checked = true;
                        }
                    }

                    disable_itemCheck = false;
                }
            }
        }
        private void btnNone_Click(object sender, EventArgs e)
        {
            ToolStripButton[] btn = { btnL1None, btnL2None, btnL3None, btnL4None };
            ListView[] lv = { lvL1Lineup, lvL2Lineup, lvL3Lineup, lvL4Lineup };

            // determine which lineup the button click is for
            for (int i = 0; i < btn.Length; ++i)
            {
                if (btn[i].Equals(sender))
                {
                    // escape if listview is not enabled
                    if (!lv[i].Enabled) break;

                    disable_itemCheck = true;
                    HashSet<string> removeStations = new HashSet<string>();

                    // scan through lineup and uncheck any checked items
                    foreach (ListViewItem item in lv[i].Items)
                    {
                        if (item.Checked)
                        {
                            string stationId = item.SubItems[2].Text;
                            removeStations.Add(stationId);
                            includedStations.Remove(stationId);
                            excludedStations.Add(stationId);
                            item.Checked = false;
                        }
                    }

                    // if there were no items to uncheck, stop
                    if (removeStations.Count == 0) break;

                    // scan through all lineups and uncheck the affected stations
                    foreach (ListView listview in lv)
                    {
                        foreach (ListViewItem item in listview.Items)
                        {
                            if (item.Checked && removeStations.Contains(item.SubItems[2].Text)) item.Checked = false;
                        }
                    }

                    disable_itemCheck = false;
                }
            }
        }
        private void lvLineup_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (disable_itemCheck) return;

            // Ignore item checks when selecting multiple items
            if ((ModifierKeys & (Keys.Shift | Keys.Control)) > 0)
            {
                e.NewValue = e.CurrentValue;
            }

            // temporarily disable item check
            disable_itemCheck = true;

            ListView[] listviews = { lvL1Lineup, lvL2Lineup, lvL3Lineup, lvL4Lineup };
            string stationId = string.Empty;

            foreach (ListView listview in listviews)
            {
                // if not the sender, go to next listview
                if (!sender.Equals(listview)) continue;

                // if listview is not focused, then this is draw event, not click event
                if (!listview.Focused)
                {
                    disable_itemCheck = false;
                    return;
                }

                // determine what station id it is
                stationId = listview.Items[e.Index].SubItems[(int)LineupColumn.StationID].Text;

                // update the include/exclude hashsets
                if (e.NewValue == CheckState.Checked)
                {
                    includedStations.Add(stationId);
                    excludedStations.Remove(stationId);
                }
                else
                {
                    includedStations.Remove(stationId);
                    excludedStations.Add(stationId);
                }
                break;
            }

            // scan all the listviews to change check state
            foreach (ListView listview in listviews)
            {
                foreach (ListViewItem item in listview.Items)
                {
                    if ((item != null) && item.SubItems[(int)LineupColumn.StationID].Text.Equals(stationId))
                    {
                        item.Checked = (e.NewValue == CheckState.Checked);
                    }
                }
            }

            // re-enable item check
            disable_itemCheck = false;
        }
        #endregion

        #region ========== Buttons & Links ==========
        private void RefreshConfiguration()
        {
            ToolStripMenuItem[] items = { L1includeToolStripMenuItem, L2includeToolStripMenuItem, L3includeToolStripMenuItem, L4includeToolStripMenuItem };
            ListView[] listViews = { lvL1Lineup, lvL2Lineup, lvL3Lineup, lvL4Lineup };
            HashSet<string> stations = new HashSet<string>();
            HashSet<string> expectedStationIds = new HashSet<string>();

            // reset included lineups and stations
            config.IncludedLineup = new List<string>();
            config.StationID = new List<SdChannelDownload>();

            // add all station includes and excludes to configuration file
            for (int i = 0; i < items.Length; ++i)
            {
                // flag that lineup is included for download and add to inluded lineups
                bool included = items[i].Checked;
                if (included) config.IncludedLineup.Add(lineups[i]);

                foreach (ListViewItem listviewitem in listViews[i].Items)
                {
                    SdChannelDownload station = (SdChannelDownload)listviewitem.Tag;
                    station.StationID = station.StationID.Replace("-", "");

                    if (!stations.Contains(listviewitem.SubItems[(int)LineupColumn.StationID].Text))
                    {
                        stations.Add(listviewitem.SubItems[(int)LineupColumn.StationID].Text);
                        if (!listviewitem.Checked)
                        {
                            station.StationID = "-" + station.StationID;
                        }
                        config.StationID.Add(station);
                    }

                    if (included && listviewitem.Checked && !expectedStationIds.Contains(station.StationID))
                    {
                        expectedStationIds.Add(station.StationID);
                    }
                }
            }
            if (L5includeToolStripMenuItem.Checked && (btnCustomLineup.DropDown.Items.Count > 0))
            {
                config.IncludedLineup.Add((string)btnCustomLineup.Tag);
                foreach (ListViewItem item in lvL5Lineup.Items)
                {
                    if (item.Checked) expectedStationIds.Add(item.SubItems[(int)LineupColumn.StationID].Text);
                }
            }
            config.ExpectedServicecount = expectedStationIds.Count;
        }
        private void btnSave_Click(object sender, EventArgs e)
        {
            RefreshConfiguration();

            // sanity checks
            if ((config.ExpectedServicecount  == 0) && (sender != null))
            {
                if (MessageBox.Show("There are no INCLUDED lineups and/or no stations selected for download.\n\nDo you wish to commit these changes?",
                                    "No Stations to Download", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                {
                    return;
                }
            }
            else if ((config.ExpectedServicecount != oldConfig.ExpectedServicecount) && (config.ExpectedServicecount > 0))
            {
                string prompt = string.Format("The number of stations to download has {0} from {1} to {2} from the previous configuration.\n\nDo you wish to commit these changes?",
                                              (oldConfig.ExpectedServicecount > config.ExpectedServicecount) ? "decreased" : "increased",
                                              oldConfig.ExpectedServicecount, config.ExpectedServicecount);
                if (MessageBox.Show(prompt, "Change in Expected Services Count", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                {
                    return;
                }
            }

            // commit the updated config file if there are changes
            if (newLogin || !config.Equals(oldConfig))
            {
                // save the file and determine flags for execution if selected
                try
                {
                    //// backup the existing config file
                    //if (File.Exists(Helper.Epg123CfgPath))
                    //{
                    //    Dictionary<string, string> backup = new Dictionary<string, string>();
                    //    backup.Add(Helper.Epg123CfgPath, Path.GetFileName(Helper.Epg123CfgPath));
                    //    CompressXmlFiles.CreatePackage(backup, "config");
                    //}

                    // save configuration file
                    using (StreamWriter stream = new StreamWriter(Helper.Epg123CfgPath, false, Encoding.UTF8))
                    {
                        config.version = Helper.epg123Version;
                        XmlSerializer serializer = new XmlSerializer(typeof(epgConfig));
                        XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
                        ns.Add("", "");
                        TextWriter writer = stream;
                        serializer.Serialize(writer, config, ns);
                    }
                    oldConfig = config.Clone();

                    newLogin = false;
                }
                catch (IOException ex)
                {
                    Logger.WriteError(ex.Message);
                }
                catch (Exception ex)
                {
                    Logger.WriteError(ex.Message);
                }
            }

            import = cbImport.Checked;
            match = cbAutomatch.Checked;
            if ((sender != null) && (Execute = sender.Equals(btnExecute))) this.Close();
        }
        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }
        private void btnClientConfig_Click(object sender, EventArgs e)
        {
            frmLineups gui = new frmLineups();
            gui.ShowDialog();
            if (!gui.cancel)
            {
                newLineups = gui.newLineups;
                buildLineupTabs();
            }
        }
        private void btnViewLog_Click(object sender, EventArgs e)
        {
            if (File.Exists(Helper.Epg123TraceLogPath))
            {
                Process.Start("notepad.exe", Helper.Epg123TraceLogPath);
            }
        }
        private void btnClearCache_Click(object sender, EventArgs e)
        {
            this.Cursor = Cursors.WaitCursor;
            if (Directory.Exists(Helper.Epg123CacheFolder))
            {
                int failed = 0;
                foreach (string file in Directory.GetFiles(Helper.Epg123CacheFolder))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        ++failed;
                    }
                }

                if (failed > 0)
                {
                    Logger.WriteError("Failed to delete all files from the cache folder.");
                }
            }

            if (File.Exists(Helper.Epg123GuideImagesXmlPath))
            {
                try
                {
                    File.Delete(Helper.Epg123GuideImagesXmlPath);
                }
                catch (Exception ex)
                {
                    Logger.WriteError(string.Format("Failed to delete image cache. Message: {0}", ex.Message));
                }
            }

            if (File.Exists(Helper.Epg123MmuiplusJsonPath))
            {
                try
                {
                    File.Delete(Helper.Epg123MmuiplusJsonPath);
                }
                catch (Exception ex)
                {
                    Logger.WriteError(string.Format("Failed to delete MMUI+ support file. Message: {0}", ex.Message));
                }
            }

            MessageBox.Show("Cache files have been removed and will be rebuilt on next update.", "Operation Complete", MessageBoxButtons.OK);
            Cursor = Cursors.Arrow;
        }
        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            linkLabel1.LinkVisited = true;
            Process.Start("http://epg123.garyan2.net");
        }
        #endregion

        #region ========== Column Sorters ==========
        private void assignColumnSorters()
        {
            ListView[] listviews = { lvL1Lineup, lvL2Lineup, lvL3Lineup, lvL4Lineup, lvL5Lineup };
            foreach (ListView listview in listviews)
            {
                // create and assign listview item sorter
                listview.ListViewItemSorter = new ListViewColumnSorter()
                {
                    SortColumn = (int)LineupColumn.CallSign,
                    Order = SortOrder.Ascending
                };
                listview.Sort();
            }
        }
        private void lvLineupSort(object sender, ColumnClickEventArgs e)
        {
            // Determine which column sorter this click applies to
            ListViewColumnSorter lvcs = (ListViewColumnSorter)((ListView)sender).ListViewItemSorter;

            // Determine if clicked column is already the column that is being sorted
            if (e.Column == lvcs.SortColumn)
            {
                // Reverse the current sort direction for this column
                lvcs.Order = (lvcs.Order == SortOrder.Ascending) ? SortOrder.Descending : SortOrder.Ascending;
            }
            else
            {
                // Set the column number that is to be sorted; default to ascending.
                lvcs.SortColumn = e.Column;
                lvcs.Order = SortOrder.Ascending;
            }

            // Perform the sort with these new sort options.
            ((ListView)sender).Sort();
        }
        #endregion

        #region ========== Include/Exclude Stations and Lineups ==========
        HashSet<string> excludedStations = new HashSet<string>();
        HashSet<string> includedStations = new HashSet<string>();
        private void populateIncludedExcludedStations(List<SdChannelDownload> list)
        {
            foreach (SdChannelDownload station in list)
            {
                if (station.StationID.StartsWith("-"))
                {
                    excludedStations.Add(station.StationID.Replace("-", ""));
                }
                else
                {
                    includedStations.Add(station.StationID);
                }
            }
        }
        private void LineupEnableToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Image[] stopLight = { Properties.Resources.GreenLight.ToBitmap(), Properties.Resources.RedLight.ToBitmap() };
            ToolStripDropDownButton[] btns = { L1IncludeExclude, L2IncludeExclude, L3IncludeExclude, L4IncludeExclude, L5IncludeExclude };
            ToolStripMenuItem[] items = { L1includeToolStripMenuItem , L2includeToolStripMenuItem , L3includeToolStripMenuItem , L4includeToolStripMenuItem , L5includeToolStripMenuItem,
                                          L1excludeToolStripMenuItem , L2excludeToolStripMenuItem , L3excludeToolStripMenuItem , L4excludeToolStripMenuItem , L5excludeToolStripMenuItem };
            ListView[] listViews = { lvL1Lineup, lvL2Lineup, lvL3Lineup, lvL4Lineup, lvL5Lineup };
            int mid = items.Length / 2;
            for (int i = 0; i < items.Length; ++i)
            {
                // determine which menuitem was clicked
                if (items[i].Equals((ToolStripMenuItem)sender))
                {
                    items[i].Checked = true;
                    items[(i + mid) % items.Length].Checked = false;
                    btns[i % mid].Image = stopLight[i / btns.Length];

                    listViews[i % listViews.Length].Enabled = (i < 5);
                    listViews[i % listViews.Length].ForeColor = (i < 5) ? Control.DefaultForeColor : Color.LightGray;
                    break;
                }
            }
        }
        private void IncludeLineup(int lineup)
        {
            ToolStripDropDownButton[] btns = { L1IncludeExclude, L2IncludeExclude, L3IncludeExclude, L4IncludeExclude, L5IncludeExclude };
            ToolStripMenuItem[] items = { L1includeToolStripMenuItem , L2includeToolStripMenuItem , L3includeToolStripMenuItem , L4includeToolStripMenuItem , L5includeToolStripMenuItem,
                                          L1excludeToolStripMenuItem , L2excludeToolStripMenuItem , L3excludeToolStripMenuItem , L4excludeToolStripMenuItem , L5excludeToolStripMenuItem };
            ListView[] listViews = { lvL1Lineup, lvL2Lineup, lvL3Lineup, lvL4Lineup, lvL5Lineup };
            if (lineup >= btns.Length) return;

            int mid = items.Length / 2;
            btns[lineup].Image = Properties.Resources.GreenLight.ToBitmap();
            listViews[lineup].Enabled = items[lineup].Checked = true;
            items[lineup + mid].Checked = false;
            listViews[lineup].ForeColor = Control.DefaultForeColor;
        }
        private void ExcludeLineup(int lineup)
        {
            ToolStripDropDownButton[] btns = { L1IncludeExclude, L2IncludeExclude, L3IncludeExclude, L4IncludeExclude, L5IncludeExclude };
            ToolStripMenuItem[] items = { L1includeToolStripMenuItem , L2includeToolStripMenuItem , L3includeToolStripMenuItem , L4includeToolStripMenuItem , L5includeToolStripMenuItem,
                                          L1excludeToolStripMenuItem , L2excludeToolStripMenuItem , L3excludeToolStripMenuItem , L4excludeToolStripMenuItem , L5excludeToolStripMenuItem};
            ListView[] listViews = { lvL1Lineup, lvL2Lineup, lvL3Lineup, lvL4Lineup, lvL5Lineup };
            if (lineup >= btns.Length) return;

            int mid = items.Length / 2;
            btns[lineup].Image = Properties.Resources.RedLight.ToBitmap();
            listViews[lineup].Enabled = items[lineup].Checked = false;
            items[lineup + mid].Checked = true;
            listViews[lineup].ForeColor = Color.LightGray;
        }
        #endregion

        #region ===== Configuration Tabs =====
        #region ========== TAB: XMLTV ==========
        private void ckXmltvConfigs_Changed(object sender, EventArgs e)
        {
            if (sender.Equals(cbXmltv))
            {
                config.CreateXmltv = ckChannelNumbers.Enabled = ckChannelLogos.Enabled = ckXmltvFillerData.Enabled = ckXmltvExtendedInfo.Enabled =
                    lblXmltvOutput.Enabled = tbXmltvOutput.Enabled = btnXmltvOutput.Enabled = lblXmltvLogosNote.Enabled = cbXmltv.Checked;
                if (!cbXmltv.Checked)
                {
                    ckUrlLogos.Enabled = ckLocalLogos.Enabled = ckSubstitutePath.Enabled = txtSubstitutePath.Enabled = false;
                    numFillerDuration.Enabled = lblFillerDuration.Enabled = lblFillerDescription.Enabled = rtbFillerDescription.Enabled = false;
                }
                else
                {
                    ckUrlLogos.Enabled = ckLocalLogos.Enabled = ckChannelLogos.Checked;
                    ckSubstitutePath.Enabled = (ckLocalLogos.Checked && ckChannelLogos.Checked);
                    txtSubstitutePath.Enabled = (ckSubstitutePath.Checked && ckLocalLogos.Checked && ckChannelLogos.Checked);
                    numFillerDuration.Enabled = lblFillerDuration.Enabled = lblFillerDescription.Enabled = rtbFillerDescription.Enabled = ckXmltvFillerData.Checked;
                }
            }
            else if (sender.Equals(ckChannelNumbers))
            {
                config.XmltvIncludeChannelNumbers = ckChannelNumbers.Checked;
            }
            else if (sender.Equals(ckChannelLogos))
            {
                ckUrlLogos.Enabled = ckLocalLogos.Enabled = ckChannelLogos.Checked;
                ckSubstitutePath.Enabled = ckLocalLogos.Checked && ckChannelLogos.Checked;
                txtSubstitutePath.Enabled = ckSubstitutePath.Checked && ckLocalLogos.Checked && ckChannelLogos.Checked;

                if (!ckChannelLogos.Checked) config.XmltvIncludeChannelLogos = "false";

                config.XmltvIncludeChannelLogos = !ckChannelLogos.Checked ? "false" : ckUrlLogos.Checked ? "url" : !ckSubstitutePath.Checked ? "local" : "substitute";
            }
            else if (sender.Equals(ckUrlLogos))
            {
                ckLocalLogos.Checked = !ckUrlLogos.Checked;
                config.XmltvIncludeChannelLogos = "url";
            }
            else if (sender.Equals(ckLocalLogos))
            {
                ckUrlLogos.Checked = !ckLocalLogos.Checked;
                ckSubstitutePath.Enabled = ckLocalLogos.Checked && cbXmltv.Checked;
                txtSubstitutePath.Enabled = ckSubstitutePath.Checked && ckLocalLogos.Checked && cbXmltv.Checked;
                if (!ckUrlLogos.Checked)
                {
                    config.XmltvIncludeChannelLogos = (ckSubstitutePath.Checked && ckLocalLogos.Checked) ? "substitute" : "local";
                }
            }
            else if (sender.Equals(ckSubstitutePath))
            {
                txtSubstitutePath.Enabled = ckSubstitutePath.Checked && ckLocalLogos.Checked && cbXmltv.Checked;
                if (!config.XmltvIncludeChannelLogos.Equals("url") && !config.XmltvIncludeChannelLogos.Equals("false"))
                {
                    config.XmltvIncludeChannelLogos = (ckSubstitutePath.Checked && ckLocalLogos.Checked) ? "substitute" : "local";
                }
            }
            else if (sender.Equals(txtSubstitutePath))
            {
                config.XmltvLogoSubstitutePath = txtSubstitutePath.Text;
            }
            else if (sender.Equals(ckXmltvFillerData))
            {
                numFillerDuration.Enabled = lblFillerDuration.Enabled = lblFillerDescription.Enabled = rtbFillerDescription.Enabled = ckXmltvFillerData.Checked && cbXmltv.Checked;
                config.XmltvAddFillerData = ckXmltvFillerData.Checked;
            }
            else if (sender.Equals(numFillerDuration))
            {
                config.XmltvFillerProgramLength = (int)numFillerDuration.Value;
            }
            else if (sender.Equals(rtbFillerDescription))
            {
                config.XmltvFillerProgramDescription = rtbFillerDescription.Text;
            }
            else if (sender.Equals(ckXmltvExtendedInfo))
            {
                config.XmltvExtendedInfoInTitleDescriptions = ckXmltvExtendedInfo.Checked;
            }
        }
        private void btnXmltvOutput_Click(object sender, EventArgs e)
        {
            FileInfo fileInfo = new FileInfo(tbXmltvOutput.Text);
            saveFileDialog1.InitialDirectory = fileInfo.DirectoryName;
            saveFileDialog1.FileName = fileInfo.Name;

            if (DialogResult.OK == saveFileDialog1.ShowDialog())
            {
                config.XmltvOutputFile = tbXmltvOutput.Text = saveFileDialog1.FileName;
            }
        }
        #endregion
        #region ========== TAB: Images ==========
        Dictionary<string, string> sdlogos = new Dictionary<string, string>();
        private void imageConfigs_Changed(object sender, EventArgs e)
        {
            if (sender.Equals(cbSeriesPosterArt))
            {
                config.SeriesPosterArt = cbSeriesPosterArt.Checked;
            }
            else if (sender.Equals(cbTMDb))
            {
                config.TMDbCoverArt = cbTMDb.Checked;
            }
            else if (sender.Equals(cbSdLogos))
            {
                config.IncludeSDLogos = lblPreferredLogos.Enabled = cmbPreferredLogos.Enabled = lblAlternateLogos.Enabled = cmbAlternateLogos.Enabled = cbSdLogos.Checked;
            }
            else if (sender.Equals(cmbPreferredLogos))
            {
                config.PreferredLogoStyle = ((Helper.PreferredLogos)cmbPreferredLogos.SelectedIndex).ToString();
                if (config.PreferredLogoStyle.Equals("none"))
                {
                    cmbAlternateLogos.SelectedIndex = cmbAlternateLogos.Items.Count - 1;
                }
            }
            else if (sender.Equals(cmbAlternateLogos))
            {
                config.AlternateLogoStyle = ((Helper.PreferredLogos)cmbAlternateLogos.SelectedIndex).ToString();
            }
        }
        private void btnSdLogos_Click(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;

            // open form and handle the sd logo downloads and image crop/resize
            frmDownloadLogos dl = new frmDownloadLogos(sdlogos);
            dl.ShowDialog();

            Cursor = Cursors.Arrow;
            return;
        }
        #endregion
        #region ========== TAB: Config ==========
        private void configs_Changed(object sender, EventArgs e)
        {
            if (sender.Equals(numDays))
            {
                config.DaysToDownload = (int)numDays.Value;
            }
            else if (sender.Equals(cbTVDB))
            {
                config.TheTVDBNumbers = cbTVDB.Checked;
            }
            else if (sender.Equals(cbPrefixTitle))
            {
                config.PrefixEpisodeTitle = cbPrefixTitle.Checked;
                cbAlternateSEFormat.Enabled = (config.PrefixEpisodeTitle || config.PrefixEpisodeDescription);
            }
            else if (sender.Equals(cbPrefixDescription))
            {
                config.PrefixEpisodeDescription = cbPrefixDescription.Checked;
                cbAlternateSEFormat.Enabled = (config.PrefixEpisodeTitle || config.PrefixEpisodeDescription);
            }
            else if (sender.Equals(cbAlternateSEFormat))
            {
                config.AlternateSEFormat = cbAlternateSEFormat.Checked;
            }
            else if (sender.Equals(cbAppendDescription))
            {
                config.AppendEpisodeDesc = cbAppendDescription.Checked;
            }
            else if (sender.Equals(cbOadOverride))
            {
                config.OADOverride = cbOadOverride.Checked;
            }
            else if (sender.Equals(cbAddNewStations))
            {
                config.AutoAddNew = cbAddNewStations.Checked;
            }
            else if (sender.Equals(cbBrandLogo))
            {
                if (cbBrandLogo.Checked)
                {
                    if (!config.PreferredLogoStyle.Equals("light") && !config.AlternateLogoStyle.Equals("light"))
                    {
                        config.BrandLogoImage = "light";
                    }
                    else config.BrandLogoImage = "dark";
                }
                else config.BrandLogoImage = "none";
            }
            else if (sender.Equals(cbModernMedia))
            {
                config.ModernMediaUiPlusSupport = cbModernMedia.Checked;
            }
        }
        #endregion
        #region ========== TAB: Task ==========
        private void configTask_Changed(object sender, EventArgs e)
        {
            if (sender.Equals(cbImport))
            {
                config.AutoImport = cbAutomatch.Enabled = cbImport.Checked;
            }
            else if (sender.Equals(cbAutomatch))
            {
                config.Automatch = cbAutomatch.Checked;
            }
        }
        #endregion
        #endregion

        private void lvL5Lineup_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (lockCustomCheckboxes && lvL5Lineup.Focused) e.NewValue = e.CurrentValue;
        }

        private void btnCustomLineup_ButtonClick(object sender, EventArgs e)
        {
            MessageBox.Show(string.Format("This feature is not yet implemented. You can manually edit the custom lineup file \"{0}\".", Helper.Epg123CustomLineupsXmlPath),
                "Not Implemented", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void lineupMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ListView[] listViews = { lvL1Lineup, lvL2Lineup, lvL3Lineup, lvL4Lineup };
            listViews[tabLineups.SelectedIndex].SelectedIndices.Clear();
            if (listViews[tabLineups.SelectedIndex].Items.Count == 0) { e.Cancel = true; return; }
        }

        private void copyToClipboardMenuItem_Click(object sender, EventArgs e)
        {
            ListView[] listViews = { lvL1Lineup, lvL2Lineup, lvL3Lineup, lvL4Lineup };
            ToolStripLabel[] labels = { lblL1Lineup, lblL2Lineup, lblL3Lineup, lblL4Lineup };

            string TextToAdd = "Lineup: " + labels[tabLineups.SelectedIndex].Text + "\r\n";
            TextToAdd += "Call Sign\tChannel\tStationID\tName\r\n";
            foreach (ListViewItem listViewItem in listViews[tabLineups.SelectedIndex].Items)
            {
                TextToAdd += string.Format("{0}\t{1}\t{2}\t{3}\r\n", listViewItem.SubItems[0].Text, listViewItem.SubItems[1].Text, listViewItem.SubItems[2].Text, listViewItem.SubItems[3].Text);
            }
            Clipboard.SetText(TextToAdd);
        }

        private bool checkHdOverride(string stationId)
        {
            foreach (SdChannelDownload station in config.StationID ?? new List<SdChannelDownload>())
            {
                if (station.StationID == stationId) return station.HDOverride;
            }
            return false;
        }
        private bool checkSdOverride(string stationId)
        {
            foreach (SdChannelDownload station in config.StationID ?? new List<SdChannelDownload>())
            {
                if (station.StationID == stationId) return station.SDOverride;
            }
            return false;
        }

        private string checkCustomCallsign(string stationId)
        {
            foreach (SdChannelDownload station in config.StationID)
            {
                if (station.StationID == stationId && !string.IsNullOrEmpty(station.customCallSign)) return station.customCallSign;
            }
            return null;
        }
        private string checkCustomServicename(string stationId)
        {
            foreach (SdChannelDownload station in config.StationID)
            {
                if (station.StationID == stationId && !string.IsNullOrEmpty(station.customServiceName)) return station.customServiceName;
            }
            return null;
        }

        private void tabTask_Enter(object sender, EventArgs e)
        {
            updateTaskPanel(true);
        }
    }
}