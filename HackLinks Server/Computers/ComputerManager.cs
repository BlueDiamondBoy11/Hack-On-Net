﻿using HackLinks_Server.Computers.Permissions;
using HackLinks_Server.Files;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HackLinks_Server.Computers
{
    class ComputerManager
    {
        List<Node> nodeList = new List<Node>();
        Server server;
        private List<File> toDelete = new List<File>();

        public ComputerManager(Server server)
        {
            this.server = server;
        }

        public Node GetNodeByIp(string ip)
        {
            foreach(Node node in nodeList)
            {
                if (node.ip == ip)
                    return node;
            }
            return null;
        }

        public void DownloadDatabase()
        {
            var conn = server.GetConnection();

            MySqlCommand sqlCommand = new MySqlCommand("SELECT * FROM computers", conn);
            using (MySqlConnection cn1 = new MySqlConnection(server.GetConnectionString()))
            {
                cn1.Open();
                using (MySqlDataReader reader = sqlCommand.ExecuteReader())
                {
                    if (reader.HasRows)
                    {
                        while (reader.Read())
                        {
                            Node newNode = null;
                            int type = reader.GetInt32(3);
                            if (type == 4)
                            {
                                newNode = new PlayerTerminal();
                                ((PlayerTerminal)newNode).ownerId = reader.GetInt32(2);
                            }
                            else
                            {
                                newNode = new Node();
                            }
                            newNode.id = reader.GetInt32(0);
                            newNode.ip = reader.GetString(1);
                            newNode.ownerId = reader.GetInt32(2);

                            MySqlCommand fileCommand = new MySqlCommand("SELECT * FROM files WHERE computerId = @0", cn1);
                            fileCommand.Parameters.Add(new MySqlParameter("0", newNode.id));
                            List<File> computerFiles = new List<File>();

                            using (MySqlDataReader fileReader = fileCommand.ExecuteReader())
                            {
                                if (fileReader.HasRows)
                                {
                                    while (fileReader.Read())
                                    {
                                        int fileType = fileReader.GetByte(3);
                                        string fileName = fileReader.GetString(1);

                                        Console.WriteLine($"Creating file {fileName} with id {fileReader.GetInt32(0)}");

                                        File newFile = newNode.fileSystem.CreateFile(fileReader.GetInt32(0), newNode, newNode.fileSystem.rootFile, fileName);

                                        newFile.isFolder = fileType == 1;

                                        newFile.ParentId = fileReader.GetInt32(2);
                                        newFile.ReadPriv = (Group) fileReader.GetInt32(8);
                                        newFile.WritePriv = (Group) fileReader.GetInt32(7);
                                        newFile.Content = fileReader.GetString(5);
                                        newFile.SetType(fileReader.GetInt32(4));

                                        computerFiles.Add(newFile);

                                        if(newFile.ParentId == 0)
                                        {
                                            newNode.SetRoot(newFile);
                                        }
                                    }
                                }
                            }

                            FixFolder(computerFiles, newNode.fileSystem.rootFile);
                            nodeList.Add(newNode);
                        }
                    }

                }
            }

            Console.WriteLine("Initializing daemons");
            foreach(Node node in nodeList)
            {
                var daemonsFolder = node.fileSystem.rootFile.GetFile("daemons");
                if (daemonsFolder == null)
                    continue;
                var autorunFile = daemonsFolder.GetFile("autorun");
                if (autorunFile == null)
                    continue;
                foreach(string line in autorunFile.Content.Split(new string[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var daemonFile = daemonsFolder.GetFile(line);
                    if (daemonFile == null)
                        continue;
                    if (daemonFile.Type != File.FileType.DAEMON)
                        continue;
                    node.LaunchDaemon(daemonFile);
                }
            }
        }

        public static IEnumerable<T> Traverse<T>(IEnumerable<T> items,
        Func<T, IEnumerable<T>> childSelector)
        {
            var stack = new Stack<T>(items);
            while (stack.Any())
            {
                var next = stack.Pop();
                yield return next;
                foreach (var child in childSelector(next))
                    stack.Push(child);
            }
        }

        public void UploadDatabase()
        {
            Console.WriteLine("Uploading Database");

            var conn = server.GetConnection();

            foreach (Node node in nodeList)
            {
                foreach (File child in Traverse(node.fileSystem.rootFile.children, file => file.children))
                {
                    if (!child.Dirty) // Our child is clean. Continue to the next
                    {
                        continue;
                    }

                    if(UpdateDbFile(child, conn))
                    {
                        Console.WriteLine($"    Updated {child.Name}");
                    }

                    child.Dirty = false;
                }
            }

            //We iterate our list backwards to avoid our indices being clobbered by removals.
            for (int i = toDelete.Count -1; i >= 0; i--)
            {
                File file = toDelete[i];
                if (DeleteDbFile(file, conn))
                {
                    Console.WriteLine($"    Deleted {file.Name}");
                    toDelete.Remove(file);
                }
                else
                {
                    Console.WriteLine($"    Can't Delete {file.Name} ID {file.id}");
                }
            }
        }

        private bool UpdateDbFile(File child, MySqlConnection conn)
        {
            MySqlCommand fileCommand = new MySqlCommand(
                "INSERT INTO files" +
                " (id, name, parentFile, type, specialType, content, computerId, writePrivilege, readPrivilege)" +
                " VALUES" +
                " (@id, @name, @parentFile, @type, @specialType, @content, @computerId, @writePrivilege, @readPrivilege)" +
                " ON DUPLICATE KEY UPDATE" +
                " name = @name," +
                " parentFile = @parentFile," +
                " specialType = @specialType," +
                " content = @content," +
                " writePrivilege = @writePrivilege," +
                " readPrivilege = @readPrivilege"
                , conn);
            fileCommand.Parameters.AddRange(new MySqlParameter[] {
                        new MySqlParameter("id", child.id),
                        new MySqlParameter("name", child.Name),
                        new MySqlParameter("parentFile", child.ParentId),
                        new MySqlParameter("type", child.isFolder ? 1 : 0),
                        new MySqlParameter("specialType", child.Type),
                        new MySqlParameter("content", child.Content),
                        new MySqlParameter("computerId", child.computerId),
                        new MySqlParameter("writePrivilege", child.WritePriv),
                        new MySqlParameter("readPrivilege", child.ReadPriv),
                    });

            int res = fileCommand.ExecuteNonQuery();

            int insertedId = (int)fileCommand.LastInsertedId;

            return  res > 0;
        }

        private bool DeleteDbFile(File file, MySqlConnection conn)
        {
            MySqlCommand fileCommand = new MySqlCommand(
            "DELETE FROM files" +
            " WHERE" +
            " id = @id"
            , conn);

            fileCommand.Parameters.AddRange(new MySqlParameter[] {
                        new MySqlParameter("id", file.id),
                    });

            return fileCommand.ExecuteNonQuery() > 0;
        }

        public Node GetNodeById(int homeId)
        {
            foreach (Node node in nodeList)
                if (node.id == homeId)
                    return node;
            return null;
        }

        public static void FixFolder(List<File> files, File rootFile)
        {
            List<File> fixedFiles = new List<File>();
            Queue<File> fileQueue = new Queue<File>();

            fileQueue.Enqueue(rootFile);

            while(fileQueue.Any())
            {
                File parent = fileQueue.Dequeue();
                Console.WriteLine($"Processing File {parent.Name} {parent.id}");

                foreach (File child in files.Where(x => x.ParentId.Equals(parent.id)))
                {
                    Console.WriteLine($"Processing Child File {child.Name} {child.id} of {parent.Name} {parent.id}");

                    child.Parent = parent;
                    parent.children.Add(child);

                    fixedFiles.Add(child);
                    if(child.IsFolder())
                    {
                        fileQueue.Enqueue(child);
                    }
                }
            }
        }

        public void AddToDelete(File file)
        {
            toDelete.Add(file);
        }
    }
}
