namespace TES_test2
{
    partial class CatalogForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CatalogForm));
            this.txtSearch1 = new System.Windows.Forms.TextBox();
            this.txtSearch2 = new System.Windows.Forms.TextBox();
            this.buttonSearch = new System.Windows.Forms.Button();
            this.dataGridViewCatalog = new System.Windows.Forms.DataGridView();
            this.btnUpdateRevit = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.numericUpDown1 = new System.Windows.Forms.NumericUpDown();
            this.comboBoxFamily = new System.Windows.Forms.ComboBox();
            this.label4 = new System.Windows.Forms.Label();
            this.pictureBoxFamily = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewCatalog)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxFamily)).BeginInit();
            this.SuspendLayout();
            // 
            // txtSearch1
            // 
            this.txtSearch1.Location = new System.Drawing.Point(131, 68);
            this.txtSearch1.Name = "txtSearch1";
            this.txtSearch1.Size = new System.Drawing.Size(59, 20);
            this.txtSearch1.TabIndex = 0;
            this.txtSearch1.Text = "0";
            // 
            // txtSearch2
            // 
            this.txtSearch2.Location = new System.Drawing.Point(131, 94);
            this.txtSearch2.Name = "txtSearch2";
            this.txtSearch2.Size = new System.Drawing.Size(59, 20);
            this.txtSearch2.TabIndex = 0;
            this.txtSearch2.Text = "0";
            // 
            // buttonSearch
            // 
            this.buttonSearch.Location = new System.Drawing.Point(131, 124);
            this.buttonSearch.Name = "buttonSearch";
            this.buttonSearch.Size = new System.Drawing.Size(75, 23);
            this.buttonSearch.TabIndex = 2;
            this.buttonSearch.Text = "Search";
            this.buttonSearch.UseVisualStyleBackColor = true;
            this.buttonSearch.Click += new System.EventHandler(this.btnSearch_Click);
            // 
            // dataGridViewCatalog
            // 
            this.dataGridViewCatalog.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.DisplayedCells;
            this.dataGridViewCatalog.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridViewCatalog.Location = new System.Drawing.Point(54, 153);
            this.dataGridViewCatalog.Name = "dataGridViewCatalog";
            this.dataGridViewCatalog.Size = new System.Drawing.Size(431, 98);
            this.dataGridViewCatalog.TabIndex = 3;
            // 
            // btnUpdateRevit
            // 
            this.btnUpdateRevit.Location = new System.Drawing.Point(237, 124);
            this.btnUpdateRevit.Name = "btnUpdateRevit";
            this.btnUpdateRevit.Size = new System.Drawing.Size(87, 23);
            this.btnUpdateRevit.TabIndex = 4;
            this.btnUpdateRevit.Text = "Insert Family";
            this.btnUpdateRevit.UseVisualStyleBackColor = true;
            this.btnUpdateRevit.Click += new System.EventHandler(this.btnUpdateRevit_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(51, 71);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(75, 13);
            this.label1.TabIndex = 5;
            this.label1.Text = "Air flow, m3/hr";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(51, 97);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(67, 13);
            this.label2.TabIndex = 5;
            this.label2.Text = "Pressure, Pa";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(234, 71);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(69, 13);
            this.label3.TabIndex = 5;
            this.label3.Text = "Tolerance, %";
            // 
            // numericUpDown1
            // 
            this.numericUpDown1.Location = new System.Drawing.Point(309, 68);
            this.numericUpDown1.Name = "numericUpDown1";
            this.numericUpDown1.Size = new System.Drawing.Size(48, 20);
            this.numericUpDown1.TabIndex = 6;
            this.numericUpDown1.Value = new decimal(new int[] {
            10,
            0,
            0,
            0});
            // 
            // comboBoxFamily
            // 
            this.comboBoxFamily.FormattingEnabled = true;
            this.comboBoxFamily.Location = new System.Drawing.Point(131, 41);
            this.comboBoxFamily.Name = "comboBoxFamily";
            this.comboBoxFamily.Size = new System.Drawing.Size(121, 21);
            this.comboBoxFamily.TabIndex = 7;
            this.comboBoxFamily.SelectedIndexChanged += new System.EventHandler(this.comboBoxFamily_SelectedIndexChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(51, 44);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(58, 13);
            this.label4.TabIndex = 5;
            this.label4.Text = "Select Fan";
            // 
            // pictureBoxFamily
            // 
            this.pictureBoxFamily.Location = new System.Drawing.Point(391, 29);
            this.pictureBoxFamily.Name = "pictureBoxFamily";
            this.pictureBoxFamily.Size = new System.Drawing.Size(123, 95);
            this.pictureBoxFamily.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBoxFamily.TabIndex = 8;
            this.pictureBoxFamily.TabStop = false;
            // 
            // CatalogForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(550, 294);
            this.Controls.Add(this.pictureBoxFamily);
            this.Controls.Add(this.comboBoxFamily);
            this.Controls.Add(this.numericUpDown1);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btnUpdateRevit);
            this.Controls.Add(this.dataGridViewCatalog);
            this.Controls.Add(this.buttonSearch);
            this.Controls.Add(this.txtSearch2);
            this.Controls.Add(this.txtSearch1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "CatalogForm";
            this.Text = "Fan Selector";
            this.Load += new System.EventHandler(this.CatalogForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewCatalog)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxFamily)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox txtSearch1;
        private System.Windows.Forms.TextBox txtSearch2;
        private System.Windows.Forms.Button buttonSearch;
        private System.Windows.Forms.DataGridView dataGridViewCatalog;
        private System.Windows.Forms.Button btnUpdateRevit;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.NumericUpDown numericUpDown1;
        internal System.Windows.Forms.ComboBox comboBoxFamily;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.PictureBox pictureBoxFamily;
    }
}