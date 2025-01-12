﻿using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Management;
using System.Security.Principal;
using System.Diagnostics;
using System.Xml;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace Affinity11
{
    public partial class Form1 : Form
    {
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;
        public const int ERROR_INVALID_FUNCTION = 1;

        [Guid("7D0F462F-4064-4862-BC7F-933E5058C10F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDxDiagContainer
        {
            void EnumChildContainerNames(uint dwIndex, string pwszContainer, uint cchContainer);
            void EnumPropNames(uint dwIndex, string pwszPropName, uint cchPropName);
            void GetChildContainer(string pwszContainer, out IDxDiagContainer ppInstance);
            void GetNumberOfChildContainers(out uint pdwCount);
            void GetNumberOfProps(out uint pdwCount);
            void GetProp(string pwszPropName, out object pvarProp);
        }

        [ComImport]
        [Guid("A65B8071-3BFE-4213-9A5B-491DA4461CA7")]
        public class DxDiagProvider { }

        [Guid("9C6B4CB0-23F8-49CC-A3ED-45A55000A6D2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDxDiagProvider
        {
            void Initialize(ref DXDIAG_INIT_PARAMS pParams);
            void GetRootContainer(out IDxDiagContainer ppInstance);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXDIAG_INIT_PARAMS
        {
            public int dwSize;
            public uint dwDxDiagHeaderVersion;
            public bool bAllowWHQLChecks;
            public IntPtr pReserved;
        };

        [DllImport("kernel32.dll",
            EntryPoint = "GetFirmwareEnvironmentVariableA",
            SetLastError = true,
            CharSet = CharSet.Unicode,
            ExactSpelling = true,
            CallingConvention = CallingConvention.StdCall)]
        public static extern int GetFirmwareType(string lpName, string lpGUID, IntPtr pBuffer, uint size);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]

        private static extern IntPtr CreateRoundRectRgn
            (
            int nLeftRect,
            int nTopRect,
            int nRightRect,
            int nBottomRect,
            int nWidthEllipse,
            int nHeightEllipse
            );

        private static string FormatBytes(long bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }

            return String.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
        }

        public static bool isUEFI()
        {
            GetFirmwareType("", "{00000000-0000-0000-0000-000000000000}", IntPtr.Zero, 0);

            if (Marshal.GetLastWin32Error() == ERROR_INVALID_FUNCTION)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        public string SecureBootStatus()
        {
            int rc = 0;
            string key = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecureBoot\State";
            string subkey = @"UEFISecureBootEnabled";
            try
            {
                object value = Registry.GetValue(key, subkey, rc);
                if (value != null)
                    rc = (int)value;
            }
            catch { }
            return $@"{(rc >= 1 ? "ON" : "OFF")}";
        }

        public string ClockSpeed()
        {
            string clockSpeed = "";
            foreach (var item in new System.Management.ManagementObjectSearcher("select MaxClockSpeed from Win32_Processor").Get())
            {
               var clockSpeedx = (uint)item["MaxClockSpeed"];
                clockSpeed = clockSpeedx.ToString();
                
            }
            return clockSpeed;
        }

        private static T GetProperty<T>(IDxDiagContainer container, string propName)
        {
            container.GetProp(propName, out object variant);
            return (T)Convert.ChangeType(variant, typeof(T));
        }

        private long GetTotalFreeSpace(string driveName)
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.Name == driveName)
                {
                    return drive.AvailableFreeSpace;
                }
            }
            return -1;
        }

        private long GetTotalSpace(string driveName)
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.Name == driveName)
                {
                    return drive.TotalSize;
                }
            }
            return -1;
        }


        public Form1()
        {
            InitializeComponent();
            Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 15, 15));
        }


        private void Form1_Load(object sender, EventArgs e)
        {
            bool bypassTPM = false;
            WindowsPrincipal pricipal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            bool hasAdministrativeRight = pricipal.IsInRole(WindowsBuiltInRole.Administrator);

            if (!hasAdministrativeRight)
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.UseShellExecute = true;
                startInfo.WorkingDirectory = Environment.CurrentDirectory;
                startInfo.FileName = Application.ExecutablePath;
                startInfo.Verb = "runas";
                try
                {
                    Process p = Process.Start(startInfo);
                    Environment.Exit(-1);

                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    bypassTPM = true;
                }

            }

            Form2 LoadingForm = new Form2();
            LoadingForm.Show();

            LoadingForm.StatusText = "Checking system requirements...";
            int diagparsed = 0;
            double diagonal = 0;
            lbl_screen.Text = "";
            screengood.Visible = true;
            screenbad.Visible = false;
            int counter = 0;
            foreach (var item in new System.Management.ManagementObjectSearcher("\\root\\wmi","SELECT * FROM WmiMonitorBasicDisplayParams").Get())
            {
                counter = counter + 1;
                double width = (byte)item["MaxHorizontalImageSize"] / 2.54;
                double height = (byte)item["MaxVerticalImageSize"] / 2.54;
                diagonal = Math.Sqrt(width * width + height * height);
                lbl_screen.Text = lbl_screen.Text + counter + ". " + diagonal.ToString("0.00") + " inch ";
                if (diagonal <= 9)
                {
                    screengood.Visible = false;
                    screenbad.Visible = true;
                }
            }


            if (isUEFI())
            {
                lbl_type.Text = "UEFI";
                bootgood.Visible = true;
                bootbad.Visible = false;
            }
            else
            {
                lbl_type.Text = "Legacy";
                bootgood.Visible = false;
                bootbad.Visible = true;
            }
            LoadingForm.StatusText = "Checking CPU speed...";
            var clockspeed = ClockSpeed();
            lbl_clockspeed.Text = clockspeed + " MHz Frequency";
            int x = Int32.Parse(clockspeed);
            if (x > 1000)
            {
                freqgood.Visible = true;
                freqbad.Visible = false;

            }
            else
            {
                freqgood.Visible = false;
                freqbad.Visible = true;
            }
            LoadingForm.StatusText = "Getting core counts...";
            int coreCount = 0;
            foreach (var item in new System.Management.ManagementObjectSearcher("select * from Win32_Processor").Get())
            {
                coreCount += int.Parse(item["NumberOfCores"].ToString());
            }
            lbl_coresnthreads.Text = coreCount + " Cores, " + Environment.ProcessorCount + " Threads";

            if (coreCount > 1)
            {
                coresgood.Visible = true;
                coresbad.Visible = false;
            }
            else
            {
                coresgood.Visible = false;
                coresbad.Visible = true;
            }
            LoadingForm.StatusText = "Checking CPU Compatibility...";
            foreach (var item in new System.Management.ManagementObjectSearcher("select * from Win32_Processor").Get())
            {
                lbl_cpu.Text = item["Name"].ToString();

                var amdbytes = Properties.Resources.amdsupport;
                string amdsupported = System.Text.Encoding.UTF8.GetString(amdbytes);

                var intelbytes = Properties.Resources.intelsupport;
                string intelsupported = System.Text.Encoding.UTF8.GetString(intelbytes);

                string supportedCPUs = amdsupported + "\n" + intelsupported;

                string myCPU = lbl_cpu.Text.ToUpper();

                bool FoundCPU = false;

                foreach (var cpu in supportedCPUs.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))

                    if (myCPU.Contains(cpu.ToUpper()))
                    {
                        FoundCPU = true;
                    }

                if (FoundCPU)
                {
                    cpugood.Visible = true;
                    cpubad.Visible = false;
                    cpuinfo.Visible = false;
                }
                else
                {
                    if (coreCount > 1 && x > 1000)
                    {
                        cpuinfo.Visible = true;
                        cpugood.Visible = false;
                        cpubad.Visible = false;

                    }
                    else
                    {
                        cpugood.Visible = false;
                        cpubad.Visible = true;
                        cpuinfo.Visible = false;
                    }
                }

            }
            LoadingForm.StatusText = "Checking Partition Types...";
            foreach (var item in new System.Management.ManagementObjectSearcher("select * from Win32_DiskPartition").Get())
            {
                if (item["Type"].ToString().Contains("System"))
                {
                    if (item["Type"].ToString().Contains("GPT"))
                    {
                        lbl_part.Text = "GPT";
                        partgood.Visible = true;
                        partbad.Visible = false;
                    }
                    else
                    {
                        lbl_part.Text = "MBR";
                        partgood.Visible = false;
                        partbad.Visible = true;
                    }

                }
            }
            LoadingForm.StatusText = "Checking Secure Boot Status...";
            lbl_secureboot.Text = SecureBootStatus();

                    if (lbl_secureboot.Text.Contains("ON"))
                    {
                        securebootgood.Visible = true;
                        securebootbad.Visible = false;
                    }
                    else
                    {
                        securebootgood.Visible = false;
                        securebootbad.Visible = true;
                    }
            long ram = 0;
            string ramstr = "";
            LoadingForm.StatusText = "Checking RAM Compatibility...";
            foreach (var item in new System.Management.ManagementObjectSearcher("select * from Win32_PhysicalMemory").Get())
            {
                ramstr = item["Capacity"].ToString();
                ram = ram += long.Parse(ramstr);
            }
            lbl_ram.Text = FormatBytes(ram).ToString();

            if (lbl_ram.Text.Contains("GB"))
            {
                string amt = lbl_ram.Text.ToString();
                string[] splitted = amt.Split(' ');
                int ramtotal = int.Parse(splitted[0]);
                if (ramtotal >= 4)
                {
                    ramgood.Visible = true;
                    rambad.Visible = false;
                }
                else
                {
                    ramgood.Visible = false;
                    rambad.Visible = true;
                }
            }
            LoadingForm.StatusText = "Checking disk size...";
            var systemdrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));

                long systemfreespace = GetTotalFreeSpace(systemdrive);
                string systemfreespacestr = FormatBytes(systemfreespace).Split(' ')[0];
                Double systemfreespacedouble = Convert.ToDouble(systemfreespacestr);
                lbl_freespace.Text = FormatBytes(systemfreespace).ToString();

                if (systemfreespacedouble >= 64)
                {
                    freespacegood.Visible = true;
                    freespaceinfo.Visible = false;
                }
                else
                {
                    freespacegood.Visible = false;
                    freespaceinfo.Visible = true;
                }


                long systemtotalspace = GetTotalSpace(systemdrive);
                string systemspacestr = FormatBytes(systemtotalspace).Split(' ')[0];
                Double systemspacedouble = Convert.ToDouble(systemspacestr);
                lbl_storage.Text = FormatBytes(systemtotalspace).ToString();

                if (systemspacedouble >= 64)
                {
                    hddgood.Visible = true;
                    hddbad.Visible = false;
                }
                else
                {
                    hddgood.Visible = false;
                    hddbad.Visible = true;
                }


            LoadingForm.StatusText = "Getting DirectX && WDDM info...";
            Process.Start("dxdiag", "/x dxv.xml");
            while (!File.Exists("dxv.xml"))
                Thread.Sleep(1000);

            XmlDocument doc = new XmlDocument();
            doc.Load("dxv.xml");
            XmlNode dxd = doc.SelectSingleNode("//DxDiag");
            XmlNode dxv = dxd.SelectSingleNode("//DirectXVersion");
            XmlNode wddmv = dxd.SelectSingleNode("//DriverModel");
            Double directXver = Convert.ToDouble(dxv.InnerText.Split(' ')[1]);
            Double wver = Convert.ToDouble(wddmv.InnerText.Split(' ')[1]);
            lbl_directx.Text = "DirectX " + directXver;
            lbl_wddm.Text = "Version: " + wver;

            if (directXver < 12)
            {
                directgood.Visible = false;
                directbad.Visible = true;
            }
            else
            {
                directgood.Visible = true;
                directbad.Visible = false;
            }

            if (wver >= 2)
            {
                wddmbad.Visible = false;
                wddmgood.Visible = true;
            }

            if (bypassTPM)
            {
                tpmgood.Visible = false;
                tpmbad.Visible = true;
                tpminfo.Visible = false;
                lbl_tpm.Text = "Cannot get TPM info without admin privileges. Run as admin and try again.";
                LoadingForm.Hide();
            }
            else
            {
            LoadingForm.StatusText = "Getting TPM version...";
            ManagementScope scope = new ManagementScope("\\\\.\\ROOT\\CIMV2\\Security\\MicrosoftTpm");
            ObjectQuery query = new ObjectQuery("SELECT * FROM Win32_Tpm");
            ManagementObjectSearcher searcher =
                                    new ManagementObjectSearcher(scope, query);
            ManagementObjectCollection queryCollection = searcher.Get();
            foreach (ManagementObject m in queryCollection)
            {
                string tpmver = m["SpecVersion"].ToString();
                string[] splitted = tpmver.Split(',');
                lbl_tpm.Text = "TPM " + splitted[0];

                if (splitted[0].Contains("2.0"))
                {
                    tpmgood.Visible = true;
                    tpmbad.Visible = false;
                    tpminfo.Visible = false;
                    LoadingForm.StatusText = "Loading results...";
                    LoadingForm.Hide();
                    return;
                }
                if (splitted[0].Contains("1.2"))
                {
                    tpminfo.Visible = false;
                    tpmgood.Visible = false;
                    tpmbad.Visible = true;
                    LoadingForm.StatusText = "Loading results...";
                    LoadingForm.Hide();
                    return;
                }

                tpmgood.Visible = false;
                tpminfo.Visible = false;
                tpmbad.Visible = true;
                LoadingForm.StatusText = "Loading results...";
                LoadingForm.Hide();
            }
            }


        }

        private void close_Click(object sender, EventArgs e)
        {
            Environment.Exit(-1);
        }

        private void Form1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void panel1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void label16_Click(object sender, EventArgs e)
        {
            this.WindowState = FormWindowState.Minimized;
        }

        private void cpuinfo_MouseHover(object sender, EventArgs e)
        {
                ToolTip tt = new ToolTip();
                tt.SetToolTip(this.cpuinfo, "Your CPU meets the soft requirements, it's just not listed on the offical list of supported processors.");
        }

        private void tpminfo_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.tpminfo, "Your TPM version meets the soft requirements. However, the recommended TPM version is 2.0.");
        }

        private void close_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.close, "Close Affinity11");
        }

        private void bootbad_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.bootbad, "Your system needs to support a UEFI boot mode, right now your system is booting using Legacy. This doesn't necessarily mean that your system doesn't support it. Check your motherboard, system manual or bios for more information.");
        }

        private void cpubad_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.cpubad, "Your CPU doesn't meet the specification requirements, see individual info about frequency or cores below.");

        }

        private void freqbad_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.freqbad, "Your CPU frequency doesn't meet the minimum requirements for Windows 11.");
        }

        private void coresbad_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.coresbad, "You don't have enough processing cores to run Windows 11.");

        }

        private void partbad_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.partbad, "Your system needs to support GPT partition types, right now your system is booting using MBR. This doesn't necessarily mean that your system doesn't support it. Check your motherboard, system manual or bios for more information.");

        }

        private void rambad_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.rambad, "Your RAM does not meet the minimum requirements for Windows 11.");
        }

        private void hddbad_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.hddbad, "Your drive does not have enough capacity to run Windows 11.");
        }

        private void tpmbad_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.tpmbad, "Your TPM version is too low, or non-existent. This doesn't necessarily mean that your system doesn't support it. Check your motherboard, system manual, or bios for more information. See TPM or PPT.");
        }

        private void directbad_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.directbad, "Your DirectX version is too low. This doesn't necessarily mean that your system doesn't support higher versions. Check DXDIAG for more information.");

        }

        private void screenbad_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.screenbad, "One or more of your monitors are too small to work on Windows 11.");

        }

        private void wddmbad_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.wddmbad, "Your Windows Display Driver Model version does not meet the minimum requirements for Windows 11.");
        }

        private void securebootbad_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.securebootbad, "Secure boot is disabled, or functionality is missing. This doesn't necessarily mean that your system doesn't support it. Check your motherboard, system manual, or bios for more information.");
        }

        private void freespaceinfo_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.freespaceinfo, "You don't have enough free space per the requirements, this doesn't mean you don't have enough total space. Just keep in mind Windows 11 requires at least 64GB of available space.");

        }
    }
}
