﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StarDisplay
{
    static class Program
    {
        /// <summary>
        /// Главная точка входа для приложения.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += new ThreadExceptionEventHandler(Form1_UIThreadException);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

            Application.Run(new MainWindow());
            Environment.Exit(Environment.ExitCode);
        }

        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            string name = args.Name.Split(new char[] { ',' })[0].Trim();
            if (name == "Octokit")
                return Assembly.Load(Resource.Octokit);
            if (name == "Newtonsoft.Json")
                return Assembly.Load(Resource.Newtonsoft_Json);
            if (name == "Archipelago.MultiClient.Net")
                return Assembly.Load(Resource.Archipelago_Multiclient_Net);

            return null;
        }

        // Handle the UI exceptions by showing a dialog box, and asking the user whether
        // or not they wish to abort execution.
        private static void Form1_UIThreadException(object sender, ThreadExceptionEventArgs t)
        {
            try
            {
                ShowThreadExceptionDialog("Windows Forms Error", t.Exception);
            }
            catch
            {
                try
                {
                    MessageBox.Show("Fatal Windows Forms Error",
                        "Fatal Windows Forms Error", MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Stop);
                }
                finally
                {
                    Application.Exit();
                }
            }
            Application.Exit();
        }

        // Handle the UI exceptions by showing a dialog box, and asking the user whether
        // or not they wish to abort execution.
        // NOTE: This exception cannot be kept from terminating the application - it can only 
        // log the event, and inform the user about it. 
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                ShowThreadExceptionDialog("Windows Forms Error", (Exception)e.ExceptionObject);
            }
            catch (Exception exc)
            {
                try
                {
                    MessageBox.Show("Fatal Non-UI Error",
                        "Fatal Non-UI Error. Could not write the error to the event log. Reason: "
                        + exc.Message, MessageBoxButtons.OK, MessageBoxIcon.Stop);
                }
                finally
                {
                    Application.Exit();
                }
            }
        }

        // Creates the error message and displays it.
        private static void ShowThreadExceptionDialog(string title, Exception e)
        {
            string errorMsg = "An application error occurred. Please send this error to @DNVIC on discord:\n\n"; //i dont want people bugging aglab over this considering 90% of errors probably wont be aglabs fault
            errorMsg = errorMsg + e.Message + "\n\nStack Trace:\n" + e.StackTrace;
            ReportForm rf = new ReportForm(e);
            rf.init();
            rf.ShowDialog();
        }
    }
}
