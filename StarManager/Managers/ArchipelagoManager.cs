using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Octokit;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Resolvers;
using System.ComponentModel;

namespace StarDisplay.Managers
{
    public class ArchipelagoManager
    {
        public ArchipelagoSession session;
        public DeathLinkService dls;
        public byte[] file1Stars;
        public int numberItemsReceived = 0;
        public Dictionary<int, string> courseIndex;
        public Dictionary<int, bool> cannons;

        public bool[] flags;
        public bool KillPlayer;
        private bool _hasCannons;
        private bool _deathLink;
        private long _deathTimeStamp;
        private string _slot;
        public ArchipelagoManager(string ip, int port, string passwd, string slot)
        {
            _slot = slot;
            flags = new bool[5] { false, false, false, false, false };
            courseIndex = new Dictionary<int, string>
            {
                { 8,  "Overworld"},
                { 12, "Course 1" },
                { 13, "Course 2" },
                { 14, "Course 3" },
                { 15, "Course 4" },
                { 16, "Course 5" },
                { 17, "Course 6" },
                { 18, "Course 7" },
                { 19, "Course 8" },
                { 20, "Course 9" },
                { 21, "Course 10" },
                { 22, "Course 11" },
                { 23, "Course 12" },
                { 24, "Course 13" },
                { 25, "Course 14" },
                { 26, "Course 15" },
                { 27, "Bowser 1" },
                { 28, "Bowser 2" },
                { 29, "Bowser 3" },
                { 30, "Slide" },
                { 31, "Metal Cap"},
                { 32, "Wing Cap" },
                { 33, "Vanish Cap" },
                { 34, "Secret 1" },
                { 35, "Secret 2" },
                { 36, "Secret 3"}
            };
            cannons = new Dictionary<int, bool>
            {
                { 8,  false},
                { 12, false },
                { 13, false },
                { 14, false },
                { 15, false },
                { 16, false },
                { 17, false },
                { 18, false },
                { 19, false },
                { 20, false },
                { 21, false },
                { 22, false },
                { 23, false },
                { 24, false },
                { 25, false },
                { 26, false },
                { 27, false },
                { 28, false },
                { 29, false },
                { 30, false },
                { 31, false },
                { 32, false },
                { 33, false },
                { 34, false },
                { 35, false },
                { 36, false },
                { 37, false }
            };
            Console.WriteLine(slot);
            session = ArchipelagoSessionFactory.CreateSession(ip, port);
            KillPlayer = false;
            _deathTimeStamp = 0;
            dls =  DeathLinkProvider.CreateDeathLinkService(session);
            LoginResult result;
            
            try
            {
                if(passwd == "")
                {
                    passwd = null;
                }
                result = session.TryConnectAndLogin("SM64 Romhack", slot, ItemsHandlingFlags.IncludeOwnItems, new Version(0,2,0), null, null, passwd);
            } catch (Exception e)
            {
                result = new LoginFailure(e.GetBaseException().Message);
            }
            if (!result.Successful) // taken from docs
            {
                LoginFailure failure = (LoginFailure)result;
                string errorMessage = $"Failed to Connect to {ip} as {slot}:";
                foreach (string error in failure.Errors)
                {
                    errorMessage += $"\n    {error}";
                }
                foreach (ConnectionRefusedError error in failure.ErrorCodes)
                {
                    errorMessage += $"\n    {error}";
                }
                throw new Exception(errorMessage);
            } else
            {
                LoginSuccessful success = (LoginSuccessful)result;
                _hasCannons = (bool)success.SlotData["Cannons"];
                Console.WriteLine(_hasCannons);
                _deathLink = (bool)success.SlotData["DeathLink"];
                if (_deathLink)
                {
                    dls.EnableDeathLink();
                    dls.OnDeathLinkReceived += (deathLinkObject) =>
                    {
                        KillPlayer = true;
                        _deathTimeStamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    };
                }
            }
            

        }
        public void Leave()
        {
            session.Socket.DisconnectAsync();
        }
        public void PrintStars()
        {
            Console.WriteLine(string.Join("-", file1Stars));

        }
        public void sendStars(byte[] Stars)
        {
            file1Stars = Stars;
            //PrintStars();
            for (int i = 0; i < file1Stars.Length; i++)
            {
                if(courseIndex.ContainsKey(i))
                {
                    BitArray bits = new BitArray(new byte[] { file1Stars[i] });
                    for (int j = 0; j < bits.Length; j++)
                    {
                        if (bits[j])
                        {
                            string locationName = courseIndex[i] + " Star " + (j+1);
                            if(j == 7 && _hasCannons)
                            {  
                                if(i == 12)
                                {
                                    locationName = courseIndex[8] + " Cannon";
                                } else
                                {
                                    locationName = courseIndex[i - 1] + " Cannon";
                                }
                            }
                            Console.WriteLine(locationName);
                            //Console.WriteLine(session.Locations.GetLocationIdFromName("SM64 Romhack", locationName));
                            session.Locations.CompleteLocationChecks(session.Locations.GetLocationIdFromName("SM64 Romhack", locationName));
                        }
                    }
                }
                else if (i == 37)
                {
                    BitArray bits = new BitArray(new byte[] { file1Stars[i] });
                    if (bits[7])
                    {
                        string locationName = courseIndex[36] + " Cannon";
                        session.Locations.CompleteLocationChecks(session.Locations.GetLocationIdFromName("SM64 Romhack", locationName));
                    }
                }
                if(i == 11) // flags
                {
                    BitArray bits = new BitArray(new byte[] { file1Stars[i] });
                    for (int j = 1; j < 6; j++)
                    {
                        if (!bits[j])
                        {
                            continue;
                        }
                        string locationName = "";
                        switch(j)
                        {
                            case 1:
                                locationName = "Wing Cap";
                                break;
                            case 2:
                                locationName = "Metal Cap";
                                break;
                            case 3:
                                locationName = "Vanish Cap";
                                break;
                            case 4:
                                locationName = "Key 1";
                                break;
                            case 5:
                                locationName = "Key 2";
                                break;

                        }
                        session.Locations.CompleteLocationChecks(session.Locations.GetLocationIdFromName("SM64 Romhack", locationName));
                    }
                }
            }
        }
        public int GetArchipelagoStars()
        {
            int stars = 0;
            int keyCounter = 0;
            int c = 8;
            foreach(ItemInfo item in session.Items.AllItemsReceived)
            {
                switch(session.Items.GetItemName(item.ItemId))
                {
                    case "Star":
                        stars++;
                        if(stars > 182 && _hasCannons == false) //contingency in the case of 8 stars per level
                        {
                            stars = 182;
                            cannons[c] = true; // theres definitely a better way to do this but idc
                            c = c == 8 ? 12 : c + 1;
                        }
                        break;
                    case "Progressive Key":
                        if (keyCounter == 1) //key 1
                        {
                            flags[0] = true; //key 2
                            keyCounter = 2;
                        } else
                        {
                            flags[1] = true;
                            keyCounter = 1;
                        }
                        break;
                    case "Key 1":
                        flags[1] = true;
                        break;
                    case "Key 2":
                        flags[0] = true;
                        break;
                    case "Metal Cap":
                        flags[3] = true;
                        break;
                    case "Wing Cap":
                        flags[4] = true;
                        break;
                    case "Vanish Cap":
                        flags[2] = true;
                        break;
                    default:
                        string itemName = session.Items.GetItemName(item.ItemId);
                        if(itemName.Contains("Cannon"))
                        {
                            string course = itemName.Substring(0,itemName.Length - 7);
                            Dictionary<string, int> d = courseIndex.ToDictionary(x => x.Value, x => x.Key);
                            int courseNum = d[course];
                            if (courseNum == 8)
                            {
                                cannons[12] = true;
                            }
                            //else if (courseNum == 36) {
                            //    continue; //idk what happens when you get a cannon in secret 3 since its the last course id. dont want to chance it and 99% of hacks dont use this level anyhow.
                            //}  above was true but 4.5 does have a cannon in secret 3. also secret 3 cannon can be unlocked by getting >128 coins in C1 YEP
                            else
                            {
                                cannons[courseNum + 1] = true;
                            }
                        }
                        break;
                }
            }
            return stars;
        }
        public void PlayerDied(int cause)
        {
            if(_deathLink)
            {
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > _deathTimeStamp + 15) //to avoid duplicate/clone deaths
                {
                    _deathTimeStamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    string CauseString = "this is not supposed to show up";
                    switch (cause) {
                        case 1:
                            CauseString = $"{_slot} fell into something which acts like quicksand.";
                            break;
                        case 2:
                            CauseString = $"{_slot} really likes spinning around!";
                            break;
                        case 3:
                            CauseString = $"{_slot} became a tasty meal.";
                            break;
                        case 4:
                            CauseString = $"{_slot} couldn't find clean air.";
                            break;
                        case 5:
                            CauseString = $"{_slot} tried to breathe water.";
                            break;
                        case 6:
                            CauseString = $"{_slot} is not a good conductor of electricity.";
                            break;
                        case 7:
                            CauseString = $"{_slot} doesn't like extreme temperatures.";
                            break;
                        case 8:
                            CauseString = $"{_slot} fell into a deep abyss.";
                            break;
                        case 9:
                            CauseString = $"The wind wasn't enough to save {_slot}";
                            break;
                        case 10:
                            CauseString = $"${_slot} died";
                            break;
                    }
                    Console.WriteLine(CauseString);
                    DeathLink dl = new DeathLink(_slot, CauseString);
                    dls.SendDeathLink(dl);
                }
            }
        }
    }
}
