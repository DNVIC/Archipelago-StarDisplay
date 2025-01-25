using StarDisplay.Managers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StarDisplay
{
    public partial class ArchipelagoForm : Form
    {
        public ArchipelagoManager am;
        public NetManager nm;
        public bool Silent = false;
        public bool isClosed = false;

        public ArchipelagoForm()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (am != null)
            {
                am.Leave();
                am = null;
                button1.Text = "Login";
                return;
            }

            try
            {
                am = new ArchipelagoManager(serverTextBox.Text, int.Parse(portTextBox.Text), textBoxCategory.Text, textBox2.Text);
                button1.Text = "Stop";

            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message, "Sync", MessageBoxButtons.OK, MessageBoxIcon.Error);
                //UpdateText(am.text);
                am = null;
                return;
            }
            UpdateText(am.flags);
            MessageBox.Show("Login Finished", "Sync", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
        }

        private void ArchipelagoForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && am != null)
            {
                e.Cancel = true;
            }
            else
            {
                isClosed = true;
            }
        }

        public void UpdateText(bool[] flags)
        {
            string text = "";
            text += "Items:\n";
            for(int i = 4; i >= 0; i--)
            {
                switch(i)
                {
                    case 0:
                        text += "\nKey 2: " + flags[i];
                        break;
                    case 1:
                        text += "\nKey 1: " + flags[i];
                        break;
                    case 2:
                        text += "\nVanish Cap: " + flags[i];
                        break;
                    case 3:
                        text += "\nMetal Cap: " + flags[i];
                        break;
                    case 4:
                        text += "Wing Cap: " + flags[i];
                        break;
                }
            }
            text += "\nStars: " + am.GetArchipelagoStars();
            text += "\nCannons: \n";
            foreach(KeyValuePair<int, bool> item in am.cannons)
            {
                if(item.Value)
                {
                    if (item.Key == 12)
                    {
                        text += am.courseIndex[8] + " Cannon\n";
                    }
                    else
                    {
                        text += am.courseIndex[item.Key - 1] + " Cannon\n";
                    }
                }
            }
            richTextBox1.Text = text;
        }

        private void textBoxCategory_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
