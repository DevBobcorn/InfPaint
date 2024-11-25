﻿using MaskCreator.Network;
using System.Windows;

namespace MaskCreator
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Exit(object sender, ExitEventArgs e)
        {
            // Make sure to disconnect from SAM server
            ClientSAM_REST.Disconnect();
        }
    }

}
