﻿using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using LiveSplit.ComponentUtil;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace StarDisplay
{
    public class MemoryManager : CachedManager
    {
        static unsafe bool ByteArrayCompare(byte[] data1, byte[] data2)
        {
            if (data1 == data2)
                return true;
            if (data1.Length != data2.Length)
                return false;

            fixed (byte* bytes1 = data1, bytes2 = data2)
            {
                int len = data1.Length;
                int rem = len % (sizeof(long) * 16);
                long* b1 = (long*)bytes1;
                long* b2 = (long*)bytes2;
                long* e1 = (long*)(bytes1 + len - rem);

                while (b1 < e1)
                {
                    if (*(b1) != *(b2) || *(b1 + 1) != *(b2 + 1) ||
                        *(b1 + 2) != *(b2 + 2) || *(b1 + 3) != *(b2 + 3) ||
                        *(b1 + 4) != *(b2 + 4) || *(b1 + 5) != *(b2 + 5) ||
                        *(b1 + 6) != *(b2 + 6) || *(b1 + 7) != *(b2 + 7) ||
                        *(b1 + 8) != *(b2 + 8) || *(b1 + 9) != *(b2 + 9) ||
                        *(b1 + 10) != *(b2 + 10) || *(b1 + 11) != *(b2 + 11) ||
                        *(b1 + 12) != *(b2 + 12) || *(b1 + 13) != *(b2 + 13) ||
                        *(b1 + 14) != *(b2 + 14) || *(b1 + 15) != *(b2 + 15))
                        return false;
                    b1 += 16;
                    b2 += 16;
                }

                for (int i = 0; i < rem; i++)
                    if (data1[len - 1 - i] != data2[len - 1 - i])
                        return false;

                return true;
            }
        }

        public const int FileLength = 0x70;
        private const int MarioStateOff = 0x18;
        private const int MarioStateLength = 0x34;
        private const int NetStateCtlLength = 0x60;
        private const int NetEnabledOff = 0x0;

        Process Process;
        MagicManager mm;

        private int previousTime;
        private byte[] oldStars;

        bool isDecomp;
        IntPtr verificationPtr; byte[] verificationData;
        IntPtr igtPtr; int igt;
        IntPtr[] filesPtr; IntPtr filePtr; byte[] stars;

        IntPtr levelPtr; byte level;
        IntPtr areaPtr; byte area;
        IntPtr redsPtr; sbyte reds;

        int restSecrets;
        int activePanels;

        IntPtr selectedStarPtr; byte selectedStar;

        IntPtr romCRCPtr; UInt16 romCRC;

        IntPtr romPtr; // should be read on demand
        IntPtr starImagePtr; // should be read on user demand 
        
        IntPtr spawnPointPtr; //byte
        IntPtr hpPtr; //short
        IntPtr menuModifierPtr; //short
        IntPtr spawnStatusPtr; //byte
        IntPtr igtigtPtr; //short
        IntPtr levelSpawnPtr; //byte

        IntPtr starsCountPtr; //short
        IntPtr bank13RamStartPtr; // int 

        IntPtr starCountAPPtr1;
        IntPtr starCountAPPtr2;
        IntPtr flagAPPtr;
        IntPtr cannonAPPtr;
        IntPtr capAPPtr;
        IntPtr keyAPPtr1;
        IntPtr keyAPPtr2;
        IntPtr toad1APPtr;
        IntPtr toad2APPtr;
        IntPtr toad3APPtr;

        IntPtr marioActionPtr;
        IntPtr marioFloorPtr;
        IntPtr marioFloorHeightPtr;
        IntPtr marioYPosPtr;

        IntPtr marioObjectPtr;
        IntPtr netMagicPtr;
        IntPtr netCodePtr;
        IntPtr netHookPtr;
        uint netStatesOff;

        public bool isStarsInvalidated = false;

        private int selectedFile;

        public int SelectedFile { get => selectedFile; set { if (selectedFile != value) isInvalidated = true; selectedFile = value; } }
        public int Igt { get => igt; set => igt = value; }
        public byte[] Stars { get => stars; set { if (stars == null || value == null || !stars.SequenceEqual(value)) { isInvalidated = true; isStarsInvalidated = true; } stars = value; } }
        public byte Level { get => level; set { if (level != value) isInvalidated = true; level = value; } }
        public byte Area { get => area; set { if (area != value) isInvalidated = true; area = value; } }
        public sbyte Reds { get => reds; set { if (reds != value) isInvalidated = true; reds = value; } }
        public byte SelectedStar { get => selectedStar; set { if (selectedStar != value) isInvalidated = true; selectedStar = value; } }
        public ushort RomCRC { get => romCRC; set { if (romCRC != value) { if (romCRC != 0) mm = null; isInvalidated = true; }; romCRC = value; } }
        public int RestSecrets { get => restSecrets; set { if (restSecrets != value) isInvalidated = true; restSecrets = value; } }
        public int ActivePanels { get => activePanels; set { if (activePanels != value) isInvalidated = true; activePanels = value; } }
        public bool IsDecomp { get => isDecomp; }

        public MemoryManager(Process process)
        {
            this.Process = process;
            oldStars = new byte[FileLength];
        }

        public bool ProcessActive()
        {
            return Process == null || Process.HasExited;
        }

        public bool isReadyToRead()
        {
            var curVerificationBytes = Process.ReadBytes(verificationPtr, verificationData.Length);
            if (!ByteArrayCompare(curVerificationBytes, verificationData))
                throw new ArgumentException("Verification failed");

            if (isDecomp)
                return true;

            // We can read mem now, let's read Igt and check if it is big enough
            int igt = Process.ReadValue<int>(igtPtr);
            if (igt < 30)
                isInvalidated = true;

            return (igt > 30);
        }

        public bool isMagicDone()
        {
            return mm != null && mm.isValid();
        }

        public void doMagic()
        {
            List<long> romPtrBaseSuggestions = new List<long>();
            List<long> ramPtrBaseSuggestions = new List<long>();

            var name = Process.ProcessName.ToLower();
            int offset = 0;

            if (name.Contains("project64") || name.Contains("wine-preloader"))
            {
                DeepPointer[] ramPtrBaseSuggestionsDPtrs = { new DeepPointer("Project64.exe", 0xD6A1C),     //1.6
                    new DeepPointer("RSP 1.7.dll", 0x4C054), new DeepPointer("RSP 1.7.dll", 0x44B5C),        //2.3.2; 2.4
                };

                DeepPointer[] romPtrBaseSuggestionsDPtrs = { new DeepPointer("Project64.exe", 0xD6A2C),     //1.6
                    new DeepPointer("RSP 1.7.dll", 0x4C050), new DeepPointer("RSP 1.7.dll", 0x44B58)        //2.3.2; 2.4
                };

                // Time to generate some addesses for magic check
                foreach (DeepPointer romSuggestionPtr in romPtrBaseSuggestionsDPtrs)
                {
                    int ptr = -1;
                    try
                    {
                        ptr = romSuggestionPtr.Deref<int>(Process);
                        romPtrBaseSuggestions.Add(ptr);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }

                foreach (DeepPointer ramSuggestionPtr in ramPtrBaseSuggestionsDPtrs)
                {
                    int ptr = -1;
                    try
                    {
                        ptr = ramSuggestionPtr.Deref<int>(Process);
                        ramPtrBaseSuggestions.Add(ptr);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }

            if (name.Contains("mupen64"))
            {
                if (name == "mupen64")
                {
                    // Current mupen releases
                    {
                        ramPtrBaseSuggestions.Add(0x00505CB0); // 1.0.9
                        ramPtrBaseSuggestions.Add(0x00505D80); // 1.0.9.1
                        ramPtrBaseSuggestions.Add(0x0050B110); // 1.0.10
                    }
                }
                else
                {
                    // Legacy mupen versions
                    Dictionary<string, int> mupenRAMSuggestions = new Dictionary<string, int>
                    {
                        { "mupen64-rerecording", 0x008EBA80 },
                        { "mupen64-pucrash", 0x00912300 },
                        { "mupen64_lua", 0x00888F60 },
                        { "mupen64-wiivc", 0x00901920 },
                        { "mupen64-RTZ", 0x00901920 },
                        { "mupen64-rrv8-avisplit", 0x008ECBB0 },
                        { "mupen64-rerecording-v2-reset", 0x008ECA90 },
                    };
                    ramPtrBaseSuggestions.Add(mupenRAMSuggestions[name]);
                }

                offset = 0x20;
            }

            if (name.Contains("retroarch"))
            {
                ramPtrBaseSuggestions.Add(0x80000000);
                romPtrBaseSuggestions.Add(0x90000000);
                offset = 0x40;
            }

            // Process.ProcessName;
            mm = new MagicManager(Process, romPtrBaseSuggestions.ToArray(), ramPtrBaseSuggestions.ToArray(), offset, name.Contains("retroarch"));

            isDecomp = mm.isDecomp;
            verificationPtr = new IntPtr((long) (mm.ramPtrBase + mm.verificationOffset));
            verificationData = mm.verificationBytes;
            igtPtr = new IntPtr((long)(mm.ramPtrBase + 0x32D580));

            // Can be found using bzero
            filesPtr = new IntPtr[4];
            filesPtr[0] = new IntPtr((long)(mm.ramPtrBase + (ulong)mm.saveBufferOffset + (ulong)mm.saveFileSize * 2 * 0));
            filesPtr[1] = new IntPtr((long)(mm.ramPtrBase + (ulong)mm.saveBufferOffset + (ulong)mm.saveFileSize * 2 * 1));
            filesPtr[2] = new IntPtr((long)(mm.ramPtrBase + (ulong)mm.saveBufferOffset + (ulong)mm.saveFileSize * 2 * 2));
            filesPtr[3] = new IntPtr((long)(mm.ramPtrBase + (ulong)mm.saveBufferOffset + (ulong)mm.saveFileSize * 2 * 3));

            levelPtr = new IntPtr((long)(mm.ramPtrBase + 0x32DDFA));
            areaPtr = new IntPtr((long)(mm.ramPtrBase + 0x33B249));
            starImagePtr = new IntPtr((long)(mm.ramPtrBase + 0x064F80 + 0x04800));
            redsPtr = new IntPtr((long)(mm.ramPtrBase + 0x3613FD));

            selectedStarPtr = new IntPtr((long)(mm.ramPtrBase + 0x1A81A3));

            romPtr = new IntPtr((long)(mm.romPtrBase + 0));
            romCRCPtr = new IntPtr((long)(mm.romPtrBase + 0x10));

            spawnPointPtr = new IntPtr((long)(mm.ramPtrBase + 0x33B248));
            hpPtr = new IntPtr((long)(mm.ramPtrBase + 0x33B21C));
            menuModifierPtr = new IntPtr((long)(mm.ramPtrBase + 0x33B23A));
            spawnStatusPtr = new IntPtr((long)(mm.ramPtrBase + 0x33B24B));
            igtigtPtr = new IntPtr((long)(mm.ramPtrBase + 0x33B26A));
            levelSpawnPtr = new IntPtr((long)(mm.ramPtrBase + 0x33B24A));

            starCountAPPtr1 = new IntPtr((long)(mm.ramPtrBase + 0x245000 + 0x35040));
            starCountAPPtr2 = new IntPtr((long)(mm.ramPtrBase + 0x245000 + 0x35074));
            flagAPPtr = new IntPtr((long)(mm.ramPtrBase + 0x245000 + 0x35198));
            cannonAPPtr = new IntPtr((long)(mm.ramPtrBase + 0x245000 + 0x35344));
            capAPPtr = new IntPtr((long)(mm.ramPtrBase + 0x245000 + 0x61FAC));
            keyAPPtr1 = new IntPtr((long)(mm.ramPtrBase + 0x245000 + 0x34DB0));
            keyAPPtr2 = new IntPtr((long)(mm.ramPtrBase + 0x245000 + 0x34DD4));
            toad1APPtr = new IntPtr((long)(mm.ramPtrBase + 0x245000 + 0x319B0));
            toad2APPtr = new IntPtr((long)(mm.ramPtrBase + 0x245000 + 0x319E4));
            toad3APPtr = new IntPtr((long)(mm.ramPtrBase + 0x245000 + 0x31A18));

            marioActionPtr = new IntPtr((long)(mm.ramPtrBase + 0x33B17C));
            marioFloorPtr = new IntPtr((long)(mm.ramPtrBase + 0x33B1D8));
            marioYPosPtr = new IntPtr((long)(mm.ramPtrBase + 0x33B1B0));
            marioFloorHeightPtr = new IntPtr((long)(mm.ramPtrBase + 0x33B1E0));

            starsCountPtr = new IntPtr((long)(mm.ramPtrBase + 0x33B218));
            bank13RamStartPtr = new IntPtr((long)(mm.ramPtrBase + 0x33B400 + 4 * 0x13));

            marioObjectPtr = new IntPtr((long)(mm.ramPtrBase + 0x361158));

            var data = Resource.NetBin;
            netMagicPtr = new IntPtr((long)(mm.ramPtrBase + 0x26004));
            netCodePtr = new IntPtr((long)(mm.ramPtrBase + 0x26000));
            netHookPtr = new IntPtr((long)(mm.ramPtrBase + 0x38a3c + 0x245000)); // 0x5840c
            netStatesOff = BitConverter.ToUInt32(data, 8) - 0x80000000;

            bool wasSet = false;
            if (!wasSet)
            {
                try
                {
                    Process.PriorityClass = ProcessPriorityClass.High;
                    wasSet = true;
                }
                catch (Exception) { }
            }
            try
            {
                using (Process p = Process.GetCurrentProcess())
                    p.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception) { }
        }

        public void PerformRead()
        {
            Igt = Process.ReadValue<int>(igtPtr);
            
            filePtr = filesPtr[SelectedFile];
            byte[] stars = Process.ReadBytes(filePtr, FileLength);
            for (int i = 0; i < FileLength; i += 4)
                Array.Reverse(stars, i, 4);
            Stars = stars;

            if (!isDecomp)
            {
                Level = Process.ReadValue<byte>(levelPtr);
                Area = Process.ReadValue<byte>(areaPtr);
                Reds = Process.ReadValue<sbyte>(redsPtr);

                RestSecrets = GetSecrets();
                ActivePanels = GetActivePanels();

                SelectedStar = Process.ReadValue<byte>(selectedStarPtr);
            }
            else
            {
                Level = 0;
                Area = 0;
                Reds = 0;

                RestSecrets = 0;
                ActivePanels = 0;

                SelectedStar = 0;
            }

            RomCRC = Process.ReadValue<UInt16>(romCRCPtr);
        }

        public void DeleteStars()
        {
            if (!isDecomp)
                if (Igt > 200 || Igt < 60) 
                    return;

            previousTime = Igt;
            byte[] data = Enumerable.Repeat((byte)0x00, FileLength).ToArray();
            IntPtr file = filesPtr[SelectedFile];
            if (!Process.WriteBytes(file, data))
            {
                throw new IOException();
            }
        }

        public byte GetCurrentStar()
        {
            return SelectedStar;
        }

        public byte GetCurrentArea()
        {
            return Area;
        }

        public byte GetCurrentLevel()
        {
            return Level;
        }

        private int GetCurrentOffset()
        {
            LevelOffsetsDescription lod = LevelInfo.FindByLevel(Level);
            if (lod == null)
                return -1;

            return lod.EEPOffset;
        }

        public TextHighlightAction GetCurrentLineAction(LayoutDescriptionEx ld, int layoutOff, int layoutLimit)
        {
            if (isDecomp)
                return null;

            int offset = GetCurrentOffset();

            int courseIndex = ld.courseDescription.Skip(layoutOff).Take(layoutLimit).ToList().FindIndex(lind => (lind is StarsLineDescription sld) ? sld.offset == offset : false);
            if (courseIndex != -1)
            {
                StarsLineDescription sld = (StarsLineDescription)ld.courseDescription[courseIndex + layoutOff];
                return new TextHighlightAction(courseIndex, false, sld.text);
            }

            int secretIndex = ld.secretDescription.Skip(layoutOff).Take(layoutLimit).ToList().FindIndex(lind => (lind is StarsLineDescription sld) ? sld.offset == offset : false);
            if (secretIndex != -1)
            {
                StarsLineDescription sld = (StarsLineDescription)ld.secretDescription[secretIndex + layoutOff];
                return new TextHighlightAction(secretIndex, true, sld.text);
            }

            return null;
        }

        static public int countStars(byte stars, int maxStar)
        {
            int answer = 0;
            for (int i = 0; i < maxStar; i++)
                answer += ((stars & (1 << i)) == 0) ? 0 : 1;
            return answer;
        }

        public sbyte GetReds()
        {
            return Reds;
        }

        public uint GetBehaviourRAMAddress(uint behav) // only bank 13 for now
        {
            uint bank13RamStart = Process.ReadValue<uint>(bank13RamStartPtr);
            uint request = 0x80000000 + bank13RamStart + behav;
            return request;
        }

        int GetSecrets()
        {
            return SearchObjects(GetBehaviourRAMAddress(0x3F1C));
        }

        int GetActivePanels()
        {
            uint request = GetBehaviourRAMAddress(0x5D8);
            return SearchObjects(request, 1) + SearchObjects(request, 2); //1 - active, 2 - finalized
        }

        int GetAllPanels()
        {
            return SearchObjects(GetBehaviourRAMAddress(0x5D8));
        }

        public Bitmap GetImage()
        {
            byte[] data = Process.ReadBytes(starImagePtr, 512);

            for (int i = 0; i < 512; i += 4) //TODO: Better ending convert
            {
                byte[] copy = new byte[4];
                copy[0] = data[i + 0];
                copy[1] = data[i + 1];
                copy[2] = data[i + 2];
                copy[3] = data[i + 3];
                data[i + 0] = copy[3];
                data[i + 1] = copy[2];
                data[i + 2] = copy[1];
                data[i + 3] = copy[0];
            }
            return FromRGBA16(data);
        }

        public byte[] GetROM()
        {
            int[] romSizesMB = new int[] { 64, 48, 32, 24, 16, 8, 1 };
            byte[] rom = null;
            int romSize = 0;
            foreach (int sizeMB in romSizesMB)
            {
                romSize = 1024 * 1024 * sizeMB;
                rom = Process.ReadBytes(romPtr, romSize);
                if (rom != null) break;
            }
            if (rom == null) return null;
            for (int i = 0; i < romSize; i += 4)
            {
                Array.Reverse(rom, i, 4);
            }
            return rom;
        }

        public UInt16 GetRomCRC()
        {
            return RomCRC;
        }

        public byte[] GetStars()
        {
            return Stars;
        }

        // Warning, not inverted
        public byte[] GetMarioState()
        {
            var objPtr = Process.ReadValue<int>(marioObjectPtr);
            if (0 == objPtr)
                return null;

            var statePtr = new IntPtr((long)(mm.ramPtrBase +  (ulong)(objPtr & 0xffffff) + (ulong)MarioStateOff));
            var bytes = Process.ReadBytes(statePtr, MarioStateLength);
            var afterObjPtr = Process.ReadValue<int>(marioObjectPtr);
            if (objPtr != afterObjPtr)
                return null;

            return bytes;
        }

        public int GetNetMagic()
        {
            return Process.ReadValue<int>(netMagicPtr);
        }

        public static Bitmap FromRGBA16(byte[] data)
        {
            Bitmap picture = new Bitmap(16, 16);
            for (int i = 0; i < 16; i++)
            {
                for (int j = 0; j < 16; j++)
                {
                    int offset = (16 * j + i) * 2;
                    int colorARGB = (data[offset + 1] & 0x01) * 255 << 24
                        | (data[offset] & 0xF8) << 16 | (data[offset] & 0xE0) << 11
                        | (data[offset] & 0x07) << 13 | (data[offset] & 0x07) << 8
                        | (data[offset + 1] & 0xC0) << 5
                        | (data[offset + 1] & 0x3E) << 2 | (data[offset + 1] & 0x38) >> 3;

                    Color c = Color.FromArgb(colorARGB);
                    picture.SetPixel(i, j, c);
                }
            }
            return picture;
        }

        public DrawActions GetDrawActions(LayoutDescriptionEx ld, ROMManager rm, byte[] otherStars, int layoutOff, int layoutLimit)
        {
            int totalReds = 0, reds = 0;
            if (!isDecomp)
            {
                try
                {
                    totalReds = rm != null ? rm.ParseReds(Level, GetCurrentStar(), GetCurrentArea()) : 0;
                    reds = GetReds();
                }
                catch (Exception) { }
                if (totalReds != 0) //Fix reds amount -- intended total amount is 8, so we should switch maximum to totalReds
                    reds += totalReds - 8;
                else //If we got any reds we might not be able to read total amount properly, so we set total amount to current reds to display only them
                    totalReds = reds;
            }


            //Operations are the same as with regular reds
            int totalSecrets = 0, secrets = 0;
            if (!isDecomp)
            {
                try
                {
                    totalSecrets = rm != null ? rm.ParseSecrets(Level, GetCurrentStar(), GetCurrentArea()) : 0;
                    secrets = totalSecrets - restSecrets;
                }
                catch (Exception) { }
            }

            //Operations are the same as with regular reds
            int totalPanels = 0, activePanels = 0;
            if (!isDecomp)
            {
                try
                {
                    totalPanels = rm != null ? rm.ParseFlipswitches(Level, GetCurrentStar(), GetCurrentArea()) : 0;
                    activePanels = ActivePanels;
                }
                catch (Exception) { }
            }

            DrawActions da = new DrawActions(ld, Stars, oldStars, otherStars, reds, totalReds, secrets, totalSecrets, activePanels, totalPanels, layoutOff, layoutLimit);
            oldStars = Stars;
            return da;
        }

        public DrawActions GetCollectablesOnlyDrawActions(LayoutDescriptionEx ld, ROMManager rm)
        {
            IntPtr file = filesPtr[SelectedFile];
            byte[] stars = Process.ReadBytes(file, FileLength);
            if (stars == null) return null;

            for (int i = 0; i < FileLength; i += 4)
                Array.Reverse(stars, i, 4);

            int totalReds = 0, reds = 0;
            if (!isDecomp)
            {
                try
                {
                    totalReds = rm != null ? rm.ParseReds(level, GetCurrentStar(), GetCurrentArea()) : 0;
                    reds = Reds;
                }
                catch (Exception) { }
                if (totalReds != 0) //Fix reds amount -- intended total amount is 8, so we should switch maximum to totalReds
                    reds += totalReds - 8;
                else //If we got any reds we might not be able to read total amount properly, so we set total amount to current reds to display only them
                    totalReds = reds;
            }


            //Operations are the same as with regular reds
            int totalSecrets = 0, secrets = 0;
            if (!isDecomp)
            {
                try
                {
                    totalSecrets = rm != null ? rm.ParseSecrets(level, GetCurrentStar(), GetCurrentArea()) : 0;
                    secrets = totalSecrets - RestSecrets;
                }
                catch (Exception) { }
            }

            //Operations are the same as with regular reds
            int totalPanels = 0, activePanels = 0;
            if (!isDecomp)
            {
                try
                {
                    totalPanels = rm != null ? rm.ParseFlipswitches(level, GetCurrentStar(), GetCurrentArea()) : 0;
                    activePanels = ActivePanels;
                }
                catch (Exception) { }
            }

            DrawActions da = new CollectablesOnlyDrawActions(ld, stars, oldStars, reds, totalReds, secrets, totalSecrets, activePanels, totalPanels);
            oldStars = stars;
            return da;
        }

        public int SearchObjects(UInt32 searchBehaviour)
        {
            int count = 0;

            UInt32 address = 0x33D488;

            for (int i = 0; i < 300 /*obj limit*/; i++)
            {
                IntPtr currentObjectPtr = new IntPtr((long) (mm.ramPtrBase + address));
                byte[] data = Process.ReadBytes(currentObjectPtr, 0x260);
                if (data is null)
                    break;

                UInt32 active = BitConverter.ToUInt32(data, 0x74);
                if (active != 0)
                {
                    UInt32 intparam = BitConverter.ToUInt32(data, 0x180);
                    UInt32 behaviour = BitConverter.ToUInt32(data, 0x20C);
                    UInt32 scriptParameter = BitConverter.ToUInt32(data, 0x0F0);

                    if (behaviour == searchBehaviour)
                    {
                        count++;
                    }
                }

                address = BitConverter.ToUInt32(data, 0x8) & 0x7FFFFFFF;
                if (address == 0x33D488 || address == 0)
                    break;
            }
            return count;
        }

        public int SearchObjects(UInt32 searchBehaviour, UInt32 state)
        {
            int count = 0;

            UInt32 address = 0x33D488;

            for (int i = 0; i < 300 /*obj limit*/; i++)
            {
                IntPtr currentObjectPtr = new IntPtr((long)(mm.ramPtrBase + address));
                byte[] data = Process.ReadBytes(currentObjectPtr, 0x260);
                if (data is null)
                    break;

                UInt32 active = BitConverter.ToUInt32(data, 0x74);
                if (active != 0)
                {
                    UInt32 intparam = BitConverter.ToUInt32(data, 0x180);
                    UInt32 behaviour = BitConverter.ToUInt32(data, 0x20C);
                    UInt32 scriptParameter = BitConverter.ToUInt32(data, 0x0F0);

                    //Console.Write("{0:X8}({1:X8}) ", behaviourActive1, scriptParameter);

                    if (behaviour == searchBehaviour && scriptParameter == state)
                    {
                        count++;
                    }
                }

                address = BitConverter.ToUInt32(data, 0x8) & 0x7FFFFFFF;
                if (address == 0x33D488 || address == 0)
                    break;
            }
            //Console.WriteLine();
            return count;
        }

        public override void InvalidateCache()
        {
            oldStars = new byte[FileLength];
            base.InvalidateCache();
        }

        public void FixStarCount(byte[] data, int maxShown)
        {
            if (isDecomp)
                return;

            int starCounter = countStars((byte)(data[0x8]), maxShown);
            // Fix star counter
            for (int i = 0xC; i <= 0x24; i++)
            {
                starCounter += countStars((byte)(data[i]), maxShown);
            }

            Process.WriteBytes(starsCountPtr, new byte[] { (byte)starCounter });
        }

        public void WriteToFile(int offset, int bit, int maxShown)
        {
            byte[] stars = new byte[FileLength];
            Stars.CopyTo(stars, 0);
            
            stars[offset] = (byte) (stars[offset] ^ (byte)(1 << bit));

            FixStarCount(stars, maxShown);

            for (int i = 0; i < FileLength; i += 4)
                Array.Reverse(stars, i, 4);

            Process.WriteBytes(filePtr, stars);

            for (int i = 0; i < FileLength; i += 4)
                Array.Reverse(stars, i, 4);

            stars.CopyTo(Stars, 0);

            isStarsInvalidated = true;
            isInvalidated = true;
        }

        public void WriteStarstoFile2(int numStars, Dictionary<int, bool> cannons)
        {
            byte[] stars = Process.ReadBytes(filesPtr[1], FileLength);
            for (int i = 0; i < FileLength; i += 4)
                Array.Reverse(stars, i, 4);

            //Console.WriteLine(string.Join("-", stars));
            if (numStars > 7)
            {
                stars[8] = (byte)(127 + (cannons[8] ? 128 : 0)); //technically right now there's no way cannons[8] will ever exist, but contingencies
                numStars -= 7;
            }
            else
            {
                stars[8] = (byte)((int)(Math.Pow(2, numStars)-1) + (cannons[8] ? 128 : 0));
                numStars = 0;
            }
            for(int i = 12; i <= 36; i++)
            {
                if(numStars > 7)
                {
                    numStars -= 7;
                    stars[i] = (byte)(127 + (cannons[i] ? 128 : 0));
                } else if (numStars > 0)
                {
                    stars[i] = (byte)((int)(Math.Pow(2, numStars) - 1) + (cannons[i] ? 128 : 0));
                    numStars = 0;
                } else
                {
                    stars[i] = (byte)((cannons[i] ? 128 : 0));
                }
            }
            stars[37] = (byte)((cannons[37] ? 128 : 0)); // 4.5 memes

            FixStarCount(stars, 7);

            for (int i = 0; i < FileLength; i += 4)
                Array.Reverse(stars, i, 4);

            Process.WriteBytes(filesPtr[1], stars);

            //for (int i = 0; i < FileLength; i += 4)
            //    Array.Reverse(stars, i, 4);


        }
        public void SetFile2Flags(bool[] flags)
        {
            byte[] file2Data = Process.ReadBytes(filesPtr[1], FileLength);
            for (int i = 0; i < FileLength; i += 4)
                Array.Reverse(file2Data, i, 4);
            byte[] file1Data = Process.ReadBytes(filesPtr[0], FileLength);
            for (int i = 0; i < FileLength; i += 4)
                Array.Reverse(file1Data, i, 4);
            file2Data[9] = file1Data[9];
            file2Data[10] = file1Data[10];
            byte importantData = file1Data[11];
            for(int i = 0; i < 5; i++) // sets keys + caps to flags, which are set by archipelago. every other flag is copied over
            {                          // because the assembly gets all flags from file 2, even though only 5 are randomized.
                if (flags[i]) importantData |= (byte)(1 << (5-i)); else importantData &= (byte)(0b11111111 ^ (1 << (5-i)));
            }
            file2Data[11] = importantData;

            FixStarCount(file2Data, 7);


            for (int i = 0; i < FileLength; i += 4)
                Array.Reverse(file2Data, i, 4);

            Process.WriteBytes(filesPtr[1], file2Data);
        }

        public void WriteToFile(byte[] data, int maxShown)
        {
            byte[] stars = data;
            if (stars == null) return;

            FixStarCount(data, maxShown);
            
            for (int i = 0; i < FileLength; i += 4)
                Array.Reverse(stars, i, 4);

            Process.WriteBytes(filePtr, stars);

            isStarsInvalidated = true;
            isInvalidated = true;
        }

        public void WriteToFile(int maxShown)
        {
            byte[] stars = (byte[]) Stars.Clone();
            if (stars == null) return;

            FixStarCount(stars, maxShown);

            for (int i = 0; i < FileLength; i += 4)
                Array.Reverse(stars, i, 4);

            Process.WriteBytes(filePtr, stars);
        }

        public void WriteNetState(int id, byte[] data)
        {
            if (data is object)
            {
                /*
                byte[] dataPreAnim = new byte[0x24];
                for (int i = 0; i < 0x24; i++)
                {
                    dataPreAnim[i] = data[i];
                }

                byte[] dataPostAnim = new byte[0xc];
                for (int i = 0; i < 0xc; i++)
                {
                    dataPostAnim[i] = data[i + 0x28];
                }

                var netStatesPtrPreAnim = new IntPtr(mm.ramPtrBase + netStatesOff + id * NetStateCtlLength + MarioStateOff);
                Process.WriteBytes(netStatesPtrPreAnim, dataPreAnim);

                var netStatesPtrPostAnim = new IntPtr(mm.ramPtrBase + netStatesOff + id * NetStateCtlLength + MarioStateOff + 0x28);
                Process.WriteBytes(netStatesPtrPostAnim, dataPostAnim);
                */
                var netStatesPtr = new IntPtr((long) (mm.ramPtrBase + netStatesOff + (ulong) (id * NetStateCtlLength) + MarioStateOff));
                Process.WriteBytes(netStatesPtr, data);

                var netEnabledPtr = new IntPtr((long)(mm.ramPtrBase + netStatesOff + (ulong)(id * NetStateCtlLength) + NetEnabledOff));
                Process.WriteBytes(netEnabledPtr, new byte[] { 1 });
            }
            else
            {
                var netEnabledPtr = new IntPtr((long)(mm.ramPtrBase + netStatesOff + (ulong)(id * NetStateCtlLength) + NetEnabledOff));
                Process.WriteBytes(netEnabledPtr, new byte[] { 0 });
            }
        }

        public string GetTitle()
        {
            Process.Refresh();
            return Process.MainWindowTitle;
        }

        public void WriteWarp(byte warpID, byte levelID, byte areaID)
        {
            Process.WriteBytes(areaPtr, new byte[] { areaID });
            Process.WriteBytes(spawnPointPtr, new byte[] { warpID });
            Process.WriteBytes(levelSpawnPtr, new byte[] { levelID });
            Process.WriteBytes(hpPtr, new byte[] { 0x80, 0x08 });
            Process.WriteBytes(menuModifierPtr, new byte[] { 0x04, 0x00 });
            Process.WriteBytes(spawnStatusPtr, new byte[] { 0x02 });
            Process.WriteBytes(igtigtPtr, new byte[] { 0x00, 0x00 });
        }

        public void WriteNetPatch()
        {
            var magic = Process.ReadValue<int>(netMagicPtr);
            if (0 == magic)
            {
                Process.WriteBytes(netCodePtr, Resource.NetBin);
                Process.WriteBytes(netHookPtr, new byte[] { 0x04, 0x98, 0x00, 0x0c });
            }
        }

        public string GetLocation()
        {
            var data = new byte[] { Level, Area };
            return Convert.ToBase64String(data);
        }

        public void KillProcess()
        {
            Process.Kill();
        }

        public void ApplyPatch()
        {
            byte[] resettest = Process.ReadBytes(starCountAPPtr1, 4);
            string s = BitConverter.ToString(resettest);
            if (s != "01-00-04-24") //test to see if patch hasn't been applied, i.e. if you reset the game.
            {
                //Console.WriteLine(s);
                Process.WriteBytes(starCountAPPtr1, new byte[] { 0x01, 0x00, 0x04, 0x24 });
                Process.WriteBytes(starCountAPPtr2, new byte[] { 0x01, 0x00, 0x04, 0x24 });
                Process.WriteBytes(flagAPPtr, new byte[] { 0x02, 0x00, 0x18, 0x24 });
                Process.WriteBytes(cannonAPPtr, new byte[] { 0x02, 0x00, 0x0E, 0x24 });
                Process.WriteBytes(capAPPtr, new byte[] { 0xF7, 0x9B, 0x0A, 0x08 });
                Process.WriteBytes(keyAPPtr1, new byte[] { 0x00, 0x00, 0x00, 0x00 });
                Process.WriteBytes(keyAPPtr2, new byte[] { 0x00, 0x00, 0x00, 0x00 });
                Process.WriteBytes(toad1APPtr, new byte[] { 0x70, 0xDA, 0x09, 0x08 });
                Process.WriteBytes(toad2APPtr, new byte[] { 0x7D, 0xDA, 0x09, 0x08 });
                Process.WriteBytes(toad3APPtr, new byte[] { 0x8A, 0xDA, 0x09, 0x08 });
            }
        }
        public int checkDeath()
        {
            if(GetMarioState() == null)
            {
                return 0; //prevent sending deaths on the title screen
            }
            byte[] action = Process.ReadBytes(marioActionPtr, 4);
            string s = BitConverter.ToString(action);

            var flrPtr = Process.ReadValue<int>(marioFloorPtr);
            if (0 != flrPtr)
            {
                var surfPtr = new IntPtr((long)(mm.ramPtrBase + (ulong)(flrPtr & 0xffffff) + 1));
                byte[] bytes = Process.ReadBytes(surfPtr, 2);

                //Console.WriteLine($"flrptr:{bytes[1]}");
                var afterFlrPtr = Process.ReadValue<int>(marioFloorPtr);
                if (flrPtr == afterFlrPtr)
                {
                    float floorheight = Process.ReadValue<float>(marioFloorHeightPtr);
                    float ypos = Process.ReadValue<float>(marioYPosPtr);
                    //Console.WriteLine($"bytes:{bytes[0]}, {bytes[1]}");
                    if (bytes[1] == 0x0A && ypos - floorheight < 2048f)
                    {
                        return 8; //death barrier
                    }
                    if (bytes[1] == 0x38 && ypos - floorheight < 2048f)
                    {
                        return 9; //wind
                    }
                }
                    
            }

            switch (s)
            {
                case "12-13-02-00":
                    return 1; //quicksand
                case "E3-22-02-30":
                    return 2; // whirlpool
                case "17-13-02-00":
                    return 3; //bubba
                case "14-13-02-00":
                    return 4; //suffocation/toxic gas
                case "C4-32-00-30":
                    return 5; //drown
                case "13-13-02-00":
                    return 6; //electrocution
            }
            if(Process.ReadValue<byte>(hpPtr + 1) == 0x00)
            {
                if(s == "38-03-02-00")
                {
                    return 0; //so electrocution gets the right death message
                }
                if(s == "B7-08-02-01") {
                    return 7; //lava
                }
                return 10; //generic
            }
            return 0;
        }

        public void setDeath()
        {
            Process.WriteBytes(hpPtr, new byte[] { 0x00, 0x00 });
        }
    }
}