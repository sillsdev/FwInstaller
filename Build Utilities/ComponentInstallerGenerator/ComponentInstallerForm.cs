using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ComponentInstallerGenerator
{
	public partial class ComponentInstallerForm : Form
	{
		private readonly InstallerInfo m_installerInfo = new InstallerInfo();
		private int m_selectedInstallerIndex;

		public ComponentInstallerForm()
		{
			InitializeComponent();
			try
			{
				m_installerInfo.Init();
			}
			catch (Exception e)
			{
				ComponentSummaryText.Text = e.Message + Environment.NewLine
					+ "You must correct this and then restart this utility.";
				CreateInstallerButton.Enabled = false;
				return;
			}
			var names = m_installerInfo.GetNamesArray();
			ComponentNamesBox.Items.AddRange(names);
			ComponentNamesBox.Items.Add("All of the above");
			ComponentNamesBox.SelectedIndex = 0;
		}

		private void OnComponentSelected(object sender, EventArgs e)
		{
			if (AllInstallersSelected())
			{
				ComponentSummaryText.Text = "Builds all installers in list";
				m_selectedInstallerIndex = -1;
			}
			else
			{
				var summary = m_installerInfo.GetSummary(ComponentNamesBox.SelectedIndex);
				ComponentSummaryText.Text = summary;
				m_selectedInstallerIndex = ComponentNamesBox.SelectedIndex;
			}
		}

		private void OnCreateInstallerClick(object sender, EventArgs e)
		{
			// Build all installer(s) in a new thread:
			var t = new Thread(LaunchBuild) {IsBackground = true};
			t.Start();
		}

		/// <summary>
		/// Entry point for new thread that performs the installer build(s).
		/// </summary>
		void LaunchBuild()
		{
			// Disable the button that launched the build:
			ThreadSafeEnableControls(false);

			if (m_selectedInstallerIndex == -1)
			{
				ThreadSafeAddSummaryText("Building all installers concurrently...", true);
				Parallel.For(0, ComponentNamesBox.Items.Count - 1, ThreadSafeCreateInstaller);
			}
			else
			{
				ThreadSafeAddSummaryText("Working...", true);
				var installerGenerator = new InstallerGenerator(m_installerInfo);
				var status = installerGenerator.CreateInstaller(m_selectedInstallerIndex);
				ThreadSafeAddSummaryText(status, false);
			}

			ThreadSafeAddSummaryText("Finished!", false);

			// Re-enable the launch button:
			ThreadSafeEnableControls(true);
		}

		private bool AllInstallersSelected()
		{
			return (ComponentNamesBox.SelectedIndex == ComponentNamesBox.Items.Count - 1);
		}

		/// <summary>
		/// Builds the installer with the specified index, in a thread-safe way, so that
		/// several installers can be built concurrently.
		/// </summary>
		/// <param name="i">The index into the list of installer names</param>
		private void ThreadSafeCreateInstaller(int i)
		{
			var installerGenerator = new InstallerGenerator(m_installerInfo);

			var names = m_installerInfo.GetNamesArray();

			ThreadSafeAddSummaryText("Starting to build " + names[i] + " installer...", false);

			var report = names[i] + ": ";

			report += installerGenerator.CreateInstaller(i);

			ThreadSafeAddSummaryText(report, false);
		}

		// This delegate enables asynchronous calls for setting
		// the text property on a TextBox control.
		delegate void SetCallbackWithTextAndBool(string text, bool erasePrevious);

		/// <summary>
		/// Adds the specified string to the bottom of the ComponentSummaryText Textbox in a thread-safe manner.
		/// </summary>
		/// <param name="msg">The text to be added</param>
		/// <param name="erasePrevious">true if current text in control should be wiped first</param>
		private void ThreadSafeAddSummaryText(string msg, bool erasePrevious)
		{
			// InvokeRequired required compares the thread ID of the
			// calling thread to the thread ID of the creating thread.
			// If these threads are different, it returns true.
			if (ComponentSummaryText.InvokeRequired)
			{
				var d = new SetCallbackWithTextAndBool(ThreadSafeAddSummaryText);
				ComponentSummaryText.Invoke(d, new object[] { msg, erasePrevious });
			}
			else
			{
				if (erasePrevious)
					ComponentSummaryText.Text = msg;
				else
					ComponentSummaryText.Text += msg;
				ComponentSummaryText.Text += Environment.NewLine;
				ScrollSummaryToBottom();
			}
		}

		private void ScrollSummaryToBottom()
		{
			ComponentSummaryText.SelectionStart = ComponentSummaryText.Text.Length;
			ComponentSummaryText.ScrollToCaret();
		}

		private void OnCloseClick(object sender, EventArgs e)
		{
			Close();
		}

		// This delegate enables asynchronous calls for setting
		// the text property on a TextBox control.
		delegate void SetTextCallbackWithBool(bool enable);

		/// <summary>
		/// Enable or disable the CreateInstallerButton in a thread-safe way.
		/// </summary>
		/// <param name="enable">true to enable the button, false to disable it.</param>
		private void ThreadSafeEnableControls(bool enable)
		{
			// InvokeRequired required compares the thread ID of the
			// calling thread to the thread ID of the creating thread.
			// If these threads are different, it returns true.
			if (ComponentSummaryText.InvokeRequired)
			{
				var d = new SetTextCallbackWithBool(ThreadSafeEnableControls);
				ComponentSummaryText.Invoke(d, new object[] { enable });
			}
			else
			{
				CreateInstallerButton.Enabled = enable;
				ComponentNamesBox.Enabled = enable;
			}
		}
	}
}
