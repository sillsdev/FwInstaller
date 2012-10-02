namespace ComponentInstallerGenerator
{
	partial class ComponentInstallerForm
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.label1 = new System.Windows.Forms.Label();
			this.ComponentNamesBox = new System.Windows.Forms.ComboBox();
			this.ComponentSummaryText = new System.Windows.Forms.TextBox();
			this.CreateInstallerButton = new System.Windows.Forms.Button();
			this.Cancel = new System.Windows.Forms.Button();
			this.SuspendLayout();
			//
			// label1
			//
			this.label1.AutoSize = true;
			this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label1.Location = new System.Drawing.Point(11, 21);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(121, 16);
			this.label1.TabIndex = 0;
			this.label1.Text = "Select Component:";
			//
			// ComponentNamesBox
			//
			this.ComponentNamesBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.ComponentNamesBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.ComponentNamesBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.ComponentNamesBox.FormattingEnabled = true;
			this.ComponentNamesBox.Location = new System.Drawing.Point(138, 18);
			this.ComponentNamesBox.Name = "ComponentNamesBox";
			this.ComponentNamesBox.Size = new System.Drawing.Size(333, 24);
			this.ComponentNamesBox.TabIndex = 1;
			this.ComponentNamesBox.SelectedIndexChanged += new System.EventHandler(this.OnComponentSelected);
			//
			// ComponentSummaryText
			//
			this.ComponentSummaryText.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.ComponentSummaryText.BackColor = System.Drawing.SystemColors.Window;
			this.ComponentSummaryText.Font = new System.Drawing.Font("Arial", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.ComponentSummaryText.Location = new System.Drawing.Point(11, 51);
			this.ComponentSummaryText.Multiline = true;
			this.ComponentSummaryText.Name = "ComponentSummaryText";
			this.ComponentSummaryText.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this.ComponentSummaryText.Size = new System.Drawing.Size(460, 79);
			this.ComponentSummaryText.TabIndex = 2;
			//
			// CreateInstallerButton
			//
			this.CreateInstallerButton.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.CreateInstallerButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.CreateInstallerButton.Location = new System.Drawing.Point(121, 146);
			this.CreateInstallerButton.Name = "CreateInstallerButton";
			this.CreateInstallerButton.Size = new System.Drawing.Size(240, 41);
			this.CreateInstallerButton.TabIndex = 3;
			this.CreateInstallerButton.Text = "Create Installer";
			this.CreateInstallerButton.UseVisualStyleBackColor = true;
			this.CreateInstallerButton.Click += new System.EventHandler(this.OnCreateInstallerClick);
			//
			// Cancel
			//
			this.Cancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.Cancel.Location = new System.Drawing.Point(396, 164);
			this.Cancel.Name = "Cancel";
			this.Cancel.Size = new System.Drawing.Size(75, 23);
			this.Cancel.TabIndex = 4;
			this.Cancel.Text = "Close";
			this.Cancel.UseVisualStyleBackColor = true;
			this.Cancel.Click += new System.EventHandler(this.OnCloseClick);
			//
			// ComponentInstallerForm
			//
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(482, 199);
			this.Controls.Add(this.Cancel);
			this.Controls.Add(this.CreateInstallerButton);
			this.Controls.Add(this.ComponentSummaryText);
			this.Controls.Add(this.ComponentNamesBox);
			this.Controls.Add(this.label1);
			this.Name = "ComponentInstallerForm";
			this.Text = "FieldWorks Component Installer Generator";
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.ComboBox ComponentNamesBox;
		private System.Windows.Forms.TextBox ComponentSummaryText;
		private System.Windows.Forms.Button CreateInstallerButton;
		private System.Windows.Forms.Button Cancel;
	}
}
