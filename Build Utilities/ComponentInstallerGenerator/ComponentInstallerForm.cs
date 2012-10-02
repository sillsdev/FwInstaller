using System;
using System.Windows.Forms;

namespace ComponentInstallerGenerator
{
	public partial class ComponentInstallerForm : Form
	{
		private readonly ComponentInstallerGenerator m_installerGenerator = new ComponentInstallerGenerator();

		public ComponentInstallerForm()
		{
			InitializeComponent();
			try
			{
				m_installerGenerator.Init();
			}
			catch (Exception e)
			{
				ComponentSummaryText.Text = e.Message + Environment.NewLine
					+ "You must correct this and then restart this utility.";
				CreateInstallerButton.Enabled = false;
				return;
			}
			var names = m_installerGenerator.GetNamesArray();
			ComponentNamesBox.Items.AddRange(names);
			ComponentNamesBox.Items.Add("All of the above");
			ComponentNamesBox.SelectedIndex = 0;
		}

		private void OnComponentSelected(object sender, EventArgs e)
		{
			if (AllInstallersSelected())
				ComponentSummaryText.Text = "Builds all installers in list";
			else
			{
				var summary = m_installerGenerator.GetSummary(ComponentNamesBox.SelectedIndex);
				ComponentSummaryText.Text = summary;
			}
		}

		private void OnCreateInstallerClick(object sender, EventArgs e)
		{
			CreateInstallerButton.Enabled = false;
			if (AllInstallersSelected())
				BuildAllInstallers();
			else
			{
				ComponentSummaryText.Text = "Working...";
				var status = m_installerGenerator.CreateInstaller(ComponentNamesBox.SelectedIndex);
				ComponentSummaryText.Text = status;
			}
			CreateInstallerButton.Enabled = true;
		}

		private bool AllInstallersSelected()
		{
			return (ComponentNamesBox.SelectedIndex == ComponentNamesBox.Items.Count - 1);
		}

		private void BuildAllInstallers()
		{
			var names = m_installerGenerator.GetNamesArray();
			var statusList = "";

			for (int i = 0; i < ComponentNamesBox.Items.Count - 1; i++)
			{
				ComponentSummaryText.Text = statusList + "Building " + names[i] + "...";
				ScrollSummaryToBottom();
				statusList += names[i] + ": " + m_installerGenerator.CreateInstaller(i) + Environment.NewLine;
			}
			ComponentSummaryText.Text = statusList;
			ScrollSummaryToBottom();
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
	}
}
