﻿using HackLinks_Server.Computers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HackLinks_Server.Daemons.Types.Irc;
using static HackLinksCommon.NetUtil;
using HackLinks_Server.Computers.Permissions;

namespace HackLinks_Server.Daemons.Types
{
    internal class IrcDaemon : Daemon
    {
        public List<IrcMessage> messages = new List<IrcMessage>();

        public SortedDictionary<string, Tuple<string, CommandHandler.Command>> daemonCommands = new SortedDictionary<string, Tuple<string, CommandHandler.Command>>()
        {
            { "irc", new Tuple<string, CommandHandler.Command>("irc send [text to send]\n    Send a message via the connected IRC daemon.", Irc) },
        };

        public override SortedDictionary<string, Tuple<string, CommandHandler.Command>> Commands
        {
            get => daemonCommands;
        }

        public override string StrType => "irc";

        public IrcDaemon(Node node) : base(node)
        {
            this.accessLevel = Group.GUEST;
        }

        public override DaemonType GetDaemonType()
        {
            return DaemonType.IRC;
        }

        public override void OnConnect(Session connectSession)
        {
            base.OnConnect(connectSession);
            connectSession.owner.Send(PacketType.MESSG, "Connected to IRC Service");
            connectSession.owner.Send(PacketType.KERNL, "state", "irc", "join");
            var commandData = new List<string>() { "state", "irc", "messg" };
            foreach (IrcMessage message in messages)
            {
                commandData.AddRange(new string[] { message.author, message.content });
            }
            connectSession.owner.Send(PacketType.KERNL, commandData.ToArray());
            SendMessage(new IrcMessage("ChanBot", connectSession.owner.username + " just logged in !"));
        }

        public override void OnDisconnect(Session disconnectSession)
        {
            base.OnDisconnect(disconnectSession);
        }

        public void SendMessage(IrcMessage message)
        {
            messages.Add(message);
            if (messages.Count > 60)
                messages.RemoveAt(0);
            foreach (Session session in this.connectedSessions)
            {
                if (session == null)
                    continue;
                session.owner.Send(PacketType.KERNL, "state", "irc", "messg", message.author, message.content);
            }
        }

        private static bool Irc(GameClient client, string[] command)
        {

            Session session = client.activeSession;

            IrcDaemon daemon = (IrcDaemon) client.activeSession.activeDaemon;

            if (command[0] == "irc")
            {
                if (command.Length < 2)
                {
                    session.owner.Send(PacketType.MESSG, "Usage : irc [send]");
                    return true;
                }
                var cmdArgs = command[1].Split(' ');
                if (cmdArgs.Length < 2)
                {
                    session.owner.Send(PacketType.MESSG, "Usage : irc [send]");
                    return true;
                }
                if (cmdArgs[0] == "send")
                {
                    var text = "";
                    for (int i = 1; i < cmdArgs.Length; i++)
                        text += cmdArgs[i] + (i != cmdArgs.Length ? " " : "");
                    daemon.SendMessage(new IrcMessage(session.owner.username, text));
                    return true;
                }
                session.owner.Send(PacketType.MESSG, "Usage : irc [send]");
                return true;
            }
            return false;
        }

        public override bool HandleDaemonCommand(GameClient client, string[] command)
        {
            if (Commands.ContainsKey(command[0]))
                return Commands[command[0]].Item2(client, command);

            return false;
        }

        public override string GetSSHDisplayName()
        {
            return "Open IRC";
        }
    }
}
