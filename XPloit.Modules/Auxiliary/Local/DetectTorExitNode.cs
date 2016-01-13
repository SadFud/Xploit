﻿using System;
using System.Net;
using XPloit.Core;
using XPloit.Core.Attributes;
using XPloit.Core.Enums;
using XPloit.Core.Helpers;

namespace Auxiliary.Local
{
    public class DetectTorExitNode : Module
    {
        #region Configure
        public override string Author { get { return "Fernando Díaz Toledano"; } }
        public override string Description { get { return "Check if a ip its a Tor exit node"; } }
        public override DateTime DisclosureDate { get { return DateTime.MinValue; } }
        public override bool IsLocal { get { return true; } }
        public override Reference[] References
        {
            get { return new Reference[] { new Reference(EReferenceType.URL, "https://check.torproject.org/exit-addresses") }; }
        }
        #endregion

        #region Properties
        [ConfigurableProperty(Required = true, Description = "Remote ip for check")]
        public IPAddress RemoteIp { get; set; }
        #endregion

        public override bool Run()
        {
            WriteInfo("Updating tor exit node list", TorHelper.UpdateTorExitNodeList(false).ToString(), ConsoleColor.Green);

            bool res = TorHelper.IsTorExitNode(RemoteIp);
            WriteInfo("Check tor exit node '" + RemoteIp.ToString() + "' results", res ? "EXIT-NODE DETECTED!" : "NOT LISTED", res ? ConsoleColor.Red : ConsoleColor.Green);

            return true;
        }
        public override ECheck Check()
        {
            WriteInfo("Updating tor exit node list", TorHelper.UpdateTorExitNodeList(false).ToString(), ConsoleColor.Green);

            bool res = TorHelper.IsTorExitNode(RemoteIp);
            WriteInfo("Check tor exit node '" + RemoteIp.ToString() + "' results", res ? "EXIT-NODE DETECTED!" : "NOT LISTED", res ? ConsoleColor.Red : ConsoleColor.Green);

            return res ? ECheck.Ok : ECheck.Error;
        }
    }
}