using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;
using System.IO;

namespace AutoIt_Extractor
{
    class Utils
    {
        internal readonly static byte[] AU3_HEADER = { 166, 77, 78, 187, 157, 105, 79, 172, 156, 73, 86, 15, 131, 211, 77, 120 };
        internal readonly static uint[] AU3_SIGN = { 0x36304145, 0x35304145, 0x3130424a, 0x3030424a };
        internal readonly static string PRINTABLE = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ!\"#$%&\'()*+,-./:;<=>?@[\\]^_`{|}~ \t\n\r\x0b\x0c";

        internal enum STATUS
        {
            OK, InvalidCompressedHeader
        }
        internal enum TYPE
        {
            Text, Binary
        }

        internal enum SOURCE_STATE
        {
            Extracted, Decompressed, Decompiled, Indented
        }

        internal static int Find(byte[] haystack, byte[] needle, int startPos = 0)
        {
            for (var i = startPos; i < haystack.Length - needle.Length+1; ++i)
            {
                int nMatches = 0;
                for (var j = 0; j < needle.Length && haystack[i + j] == needle[j]; ++j)
                {
                    nMatches++;
                }
                if (nMatches == needle.Length)
                    return i;
            }
            return -1;
        }

        internal static int[] FindAll(byte[] haystack, byte[] needle)
        {
            var indices = new List<int>();
            int pos = -1;
            while (pos < haystack.Length)
            {
                pos = Find(haystack, needle, pos + 1);
                if (pos == -1) break;
                indices.Add(pos);
            }
            return indices.ToArray<int>();
        }

        internal static uint GetCheckSum(byte[] buffer)
        {
            uint ebx = 0, esi = 0;
            foreach (byte b in buffer)
            {
                ebx = (b + ebx) % 0xfff1;
                esi = (esi + ebx) % 0xfff1;
            }
            return (esi << 0x10) + ebx;
        }
    }

    class AU3_Resource
    {
        const int LIMIT = 1024;
        internal string Dump()
        {
            var trailer = "\r\n\r\nClick 'Save Resource' to dump the entire data!\r\n";
            Func<string,string> limitText = e =>
            {
                if (e.Length < LIMIT)
                    return e;
                else
                    return e.Substring(0, LIMIT) + trailer;
            };
            if (Tag.Contains("SCRIPT<"))
            {
                if (SourceCode.Length > 0)
                {
                    return limitText(SourceCode);
                }
                if (Tag.Contains("UNICODE"))
                {
                    if (RawData.Length < LIMIT)
                        return Encoding.Unicode.GetString(RawData);
                    var d = new byte[LIMIT];
                    Array.Copy(RawData, d, LIMIT);
                    return Encoding.Unicode.GetString(d) + trailer;
                }
                else
                {
                    if (RawData.Length < LIMIT)
                        return Encoding.ASCII.GetString(RawData);
                    var d = new byte[LIMIT];
                    Array.Copy(RawData, d, LIMIT);
                    return Encoding.ASCII.GetString(d) + trailer;
                }
            }

            byte[] ptr = RawData;
            if (ptr == null)
                return string.Empty;

            if (ptr.All(e => Utils.PRINTABLE.Contains((char)e)))
            {
                Type = Utils.TYPE.Text;
                if (ptr.Length < LIMIT)
                    return Encoding.ASCII.GetString(ptr);
                var d = new byte[LIMIT];
                Array.Copy(ptr, d, LIMIT);
                return Encoding.ASCII.GetString(d) + trailer;
            }

            Type = Utils.TYPE.Binary;
            var buf = new StringBuilder();
            int n = 12, c = 0;
            foreach (var e in ptr)
            {
                if (c >= LIMIT)
                {
                    buf.Append(trailer);
                    break;
                }
                if (n == 0)
                {
                    buf.Append("\r\n");
                    n = 12;
                    c++;
                }
                n--;
                buf.Append(e.ToString("X2"));
                c += 2;
                buf.Append(' ');
            }
            return buf.ToString();
        }

        internal string Tag
        {
            get { return m_Tag; }
            set
            {
                m_Tag = value;
                int pos = m_Tag.LastIndexOf('\\');
                var ans = m_Tag;
                if (pos >= 0)
                    ans = ans.Substring(pos + 1);
                ShortTag = ans;
            }
        }
        internal string ShortTag { get; private set; }
        internal string Path { get; set; }
        internal bool IsCompressed { get; set; }
        internal uint CompressedSize { get; set; }
        internal uint DecompressedSize { get; set; }
        internal ulong CreationTime { get; set; }
        internal ulong LastWriteTime { get; set; }
        internal byte[] RawData { get; set; }
        internal uint RawDataSize { get; set; }
        internal int CheckSum { get; set; }
        internal string SourceCode { get; set; }
        internal Utils.STATUS Status { get; set; }
        internal Utils.TYPE Type { get; set; }
        internal string State { get; set; }

        internal Utils.SOURCE_STATE SourceState { get; set; }
        public bool IsUnicode { get; internal set; }

        public override string ToString() => ShortTag;

        internal int pos, count;
        internal uint m_254, m_258, m_250, m_264, m_260, m_268;
        internal uint ans, spos, r, v, delta;
        internal byte[] pMem;
        private string m_Tag;
        internal ProbablyHuffman[] mem24c, mem25c;
        internal event EventHandler OnComplete;
        internal bool IsComplete { get; set; }

        internal event EventHandler Update;

        internal void MarkComplete()
        {
            IsComplete = true;
            new Thread(() => OnComplete(this, null)).Start();
        }

        internal void DoUpdate()
        {
            if (Update != null)
            {
                new Thread(() => Update(this, null)).Start();
            }
        }

        internal struct ProbablyHuffman
        {
            internal uint C;
            internal int A;
            internal int D;
            internal int B;
            internal uint E;
            internal int F;
        }

        internal AU3_Resource()
        {
            //evtNotify = new AutoResetEvent(false);
            State = "";
            SourceCode = "";
            IsComplete = false;
        }

        internal void JB01_Decompress(Keys k)
        {
            count = 0;
            pos = 0;
            k.ExtractBits(this, 8);
            k.ExtractBits(this, 8);
            k.ExtractBits(this, 8);
            k.ExtractBits(this, 8);

            var size = k.ExtractBits(this, 0x10) << 16;
            size |= k.ExtractBits(this, 0x10);
            uint i = 0;
            while (i < size)
            {
                if (k.ExtractBits(this, 1) == 0)
                {
                    pMem[i++] = (byte) k.ExtractBits(this, 8);
                }
                else
                {
                    var j = i - 3 - k.ExtractBits(this, 13);
                    var times = 3 + k.ExtractBits(this, 4);

                    while (times-- > 0)
                    {
                        pMem[i++] = pMem[j++];
                    }
                }
            }
        }
        
        public void DecompressorInitLegacy()
        {
            mem24c = new ProbablyHuffman[575];
            mem25c = new ProbablyHuffman[63];

            for (int i = 0; i < 0x120; ++i)
            {
                mem24c[i].C = 1u;
                mem24c[i].B = i;
                mem24c[i].E = (uint)i;
            }

            sub_4173d8(mem24c, 0x120, 0);
            m_254 = 0;
            m_258 = 0x48;
            m_250 = 0x48;

            for (int i = 0; i < 0x20; ++i)
            {
                mem25c[i].C = 1u;
                mem25c[i].B = i;
                mem25c[i].E = (uint)i;
            }

            sub_4173d8(mem25c, 0x20, 0);
            m_264 = 0;
            m_268 = 8;
            m_260 = 8;
        }

        private void sub_4173d8(ProbablyHuffman[] a, int b, int c)
        {
            bool doLoop = true;
            int edi = b;
            var v10 = 2 * edi - 1;
            while (doLoop)
            {
                var ebx = 0u;
                var esi = 0u;
                for (int i = 0; i < edi; ++i)
                    a[i].A = 1;
                bool bAssign = edi == v10;
                do
                {
                    var vC = 0xffffffff;
                    var ecx = vC;
                    for (int i = 0; i < edi; ++i)
                    {
                        if (a[i].A != 0 && a[i].C < ecx)
                        {
                            if (a[i].C >= vC)
                            {
                                esi = (uint)i;
                                ecx = a[i].C;
                            }
                            else
                            {
                                esi = ebx;
                                ecx = vC;
                                ebx = (uint)i;
                                vC = a[i].C;
                            }
                        }
                    }
                    a[ebx].A = a[esi].A = 0;
                    a[edi].C = a[ebx].C + a[esi].C;
                    a[edi].B = (int)ebx;
                    a[edi].A = 1;
                    a[edi].E = esi;
                    a[ebx].D = edi;
                    a[esi].D = edi;
                    a[ebx].F = 0;
                    a[esi].F = 1;
                    edi++;
                } while (edi != v10);
                doLoop = false;
                if (!bAssign)
                    edi = b;
                for (int i = 0; i < edi; ++i)
                {
                    int j = i;
                    var Esi = 0;
                    while (j < 2*edi-2)
                    {
                        j = a[j].D;
                        Esi++;
                    }
                    if (Esi > 0x10)
                    {
                        doLoop = true;
                        break;
                    }
                }
                if (doLoop)
                {
                    for (int i = 0; i < edi; ++i)
                    {
                        a[i].C = 1 + (a[i].C >> 2);
                    }
                }
            }
            if (c > 0)
            {
                for (int i = 0; i < edi; ++i)
                {
                    a[i].C = 1 + (a[i].C >> c);
                }
            }
        }

        internal uint sub_417594(ProbablyHuffman[] p, int n, Keys k)
        {
            var i = 2 * n - 2;
            while (p[i].B != i)
            {
                var r = k.ExtractBits(this, 1);
                if (r > 0)
                    i = (int)p[i].E;
                else
                    i = p[i].B;
            }
            return (uint)i;
        }

        internal uint sub_417665(uint n, Keys k)
        {
            if (n <= 0x107)
                return n - 0x100;
            else
            {
                var v = (int)(1 + (n - 0x108 >> 2));
                long r = k.ExtractBits(this, (int)v);
                r += (1 << v + 2) + ((n - 0x108 & 3) << v);
                return (uint)r;
            }
        }

        internal uint sub_4176a5(Keys k)
        {
            var r = sub_417594(mem25c, 0x20, k);
            mem25c[r].C++;
            if (r > 3)
            {
                var v = (int)(1 + (r - 4 >> 1));
                var b = k.ExtractBits(this, v);
                r = b + (uint)((1L << v + 1) + ((r - 4L & 1) << v));
            }
            if (--m_260 > 0)
                return r;
            if (m_264 != 0)
            {
                m_260 = 0x180;
                sub_4173d8(mem25c, 0x20, 1);
            }
            else
            {
                m_268 += 8;
                if (m_268 >= 0x180)
                    m_264 = 1;
                m_260 = 8;
                sub_4173d8(mem25c, 0x20, 0);
            }
            return r;
        }

        internal uint sub_417518(Keys k)
        {
            var r = sub_417594(mem24c, 0x120, k);
            mem24c[r].C++;
            if (--m_250 > 0)
                return r;
            if (m_254 != 0)
            {
                m_250 = 0xd80;
                sub_4173d8(mem24c, 0x120, 1);
            }
            else
            {
                m_258 += 0x48;
                if (m_258 >= 0xd80)
                    m_254 = 1;
                m_250 = 0x48;
                sub_4173d8(mem24c, 0x120, 0);
            }
            return r;
        }

        internal void Decompile()
        {
            //evtDecompressed.WaitOne();
            //SourceCode = string.Empty;
            if (! Tag.Contains("SCRIPT<"))
            {
                //evtDecompiled.Set();
                return;
            }

            if (Status != Utils.STATUS.OK || SourceState == Utils.SOURCE_STATE.Decompiled || SourceState == Utils.SOURCE_STATE.Indented)
            {
                //evtDecompiled.Set();
                return;
            }

            if (RawData.All(e => Utils.PRINTABLE.Contains((char)e)) || Tag.Contains("UNICODE"))
            {
                return;
            }

            State = "Decompiling ...";
            DoUpdate();
            var buffer = new StringBuilder();
            int off = 0;
            var tokens = new List<string>();
            int nLines = BitConverter.ToInt32(RawData, off);
            off += 4;

            string[] KEYWORDS = {
                "", "AND", "OR", "NOT", "IF", "THEN", "ELSE", "ELSEIF", "ENDIF", "WHILE",
                "WEND", "DO", "UNTIL", "FOR", "NEXT", "TO", "STEP", "IN", "EXITLOOP",
                "CONTINUELOOP", "SELECT", "CASE", "ENDSELECT", "SWITCH", "ENDSWITCH",
                "CONTINUECASE", "DIM", "REDIM", "LOCAL", "GLOBAL", "CONST", "STATIC",
                "FUNC", "ENDFUNC", "RETURN", "EXIT", "BYREF", "WITH", "ENDWITH", "TRUE",
                "FALSE", "DEFAULT", "NULL", "VOLATILE", "ENUM"
            };

            string[] FUNCTIONS = { "ABS", "ACOS", "ADLIBREGISTER", "ADLIBUNREGISTER", "ASC", "ASCW", "ASIN", "ASSIGN", "ATAN", "AUTOITSETOPTION", "AUTOITWINGETTITLE", "AUTOITWINSETTITLE", "BEEP", "BINARY", "BINARYLEN", "BINARYMID", "BINARYTOSTRING", "BITAND", "BITNOT", "BITOR", "BITROTATE", "BITSHIFT", "BITXOR", "BLOCKINPUT", "BREAK", "CALL", "CDTRAY", "CEILING", "CHR", "CHRW", "CLIPGET", "CLIPPUT", "CONSOLEREAD", "CONSOLEWRITE", "CONSOLEWRITEERROR", "CONTROLCLICK", "CONTROLCOMMAND", "CONTROLDISABLE", "CONTROLENABLE", "CONTROLFOCUS", "CONTROLGETFOCUS", "CONTROLGETHANDLE", "CONTROLGETPOS", "CONTROLGETTEXT", "CONTROLHIDE", "CONTROLLISTVIEW", "CONTROLMOVE", "CONTROLSEND", "CONTROLSETTEXT", "CONTROLSHOW", "CONTROLTREEVIEW", "COS", "DEC", "DIRCOPY", "DIRCREATE", "DIRGETSIZE", "DIRMOVE", "DIRREMOVE", "DLLCALL", "DLLCALLADDRESS", "DLLCALLBACKFREE", "DLLCALLBACKGETPTR", "DLLCALLBACKREGISTER", "DLLCLOSE", "DLLOPEN", "DLLSTRUCTCREATE", "DLLSTRUCTGETDATA", "DLLSTRUCTGETPTR", "DLLSTRUCTGETSIZE", "DLLSTRUCTSETDATA", "DRIVEGETDRIVE", "DRIVEGETFILESYSTEM", "DRIVEGETLABEL", "DRIVEGETSERIAL", "DRIVEGETTYPE", "DRIVEMAPADD", "DRIVEMAPDEL", "DRIVEMAPGET", "DRIVESETLABEL", "DRIVESPACEFREE", "DRIVESPACETOTAL", "DRIVESTATUS", "DUMMYSPEEDTEST", "ENVGET", "ENVSET", "ENVUPDATE", "EVAL", "EXECUTE", "EXP", "FILECHANGEDIR", "FILECLOSE", "FILECOPY", "FILECREATENTFSLINK", "FILECREATESHORTCUT", "FILEDELETE", "FILEEXISTS", "FILEFINDFIRSTFILE", "FILEFINDNEXTFILE", "FILEFLUSH", "FILEGETATTRIB", "FILEGETENCODING", "FILEGETLONGNAME", "FILEGETPOS", "FILEGETSHORTCUT", "FILEGETSHORTNAME", "FILEGETSIZE", "FILEGETTIME", "FILEGETVERSION", "FILEINSTALL", "FILEMOVE", "FILEOPEN", "FILEOPENDIALOG", "FILEREAD", "FILEREADLINE", "FILEREADTOARRAY", "FILERECYCLE", "FILERECYCLEEMPTY", "FILESAVEDIALOG", "FILESELECTFOLDER", "FILESETATTRIB", "FILESETEND", "FILESETPOS", "FILESETTIME", "FILEWRITE", "FILEWRITELINE", "FLOOR", "FTPSETPROXY", "FUNCNAME", "GUICREATE", "GUICTRLCREATEAVI", "GUICTRLCREATEBUTTON", "GUICTRLCREATECHECKBOX", "GUICTRLCREATECOMBO", "GUICTRLCREATECONTEXTMENU", "GUICTRLCREATEDATE", "GUICTRLCREATEDUMMY", "GUICTRLCREATEEDIT", "GUICTRLCREATEGRAPHIC", "GUICTRLCREATEGROUP", "GUICTRLCREATEICON", "GUICTRLCREATEINPUT", "GUICTRLCREATELABEL", "GUICTRLCREATELIST", "GUICTRLCREATELISTVIEW", "GUICTRLCREATELISTVIEWITEM", "GUICTRLCREATEMENU", "GUICTRLCREATEMENUITEM", "GUICTRLCREATEMONTHCAL", "GUICTRLCREATEOBJ", "GUICTRLCREATEPIC", "GUICTRLCREATEPROGRESS", "GUICTRLCREATERADIO", "GUICTRLCREATESLIDER", "GUICTRLCREATETAB", "GUICTRLCREATETABITEM", "GUICTRLCREATETREEVIEW", "GUICTRLCREATETREEVIEWITEM", "GUICTRLCREATEUPDOWN", "GUICTRLDELETE", "GUICTRLGETHANDLE", "GUICTRLGETSTATE", "GUICTRLREAD", "GUICTRLRECVMSG", "GUICTRLREGISTERLISTVIEWSORT", "GUICTRLSENDMSG", "GUICTRLSENDTODUMMY", "GUICTRLSETBKCOLOR", "GUICTRLSETCOLOR", "GUICTRLSETCURSOR", "GUICTRLSETDATA", "GUICTRLSETDEFBKCOLOR", "GUICTRLSETDEFCOLOR", "GUICTRLSETFONT", "GUICTRLSETGRAPHIC", "GUICTRLSETIMAGE", "GUICTRLSETLIMIT", "GUICTRLSETONEVENT", "GUICTRLSETPOS", "GUICTRLSETRESIZING", "GUICTRLSETSTATE", "GUICTRLSETSTYLE", "GUICTRLSETTIP", "GUIDELETE", "GUIGETCURSORINFO", "GUIGETMSG", "GUIGETSTYLE", "GUIREGISTERMSG", "GUISETACCELERATORS", "GUISETBKCOLOR", "GUISETCOORD", "GUISETCURSOR", "GUISETFONT", "GUISETHELP", "GUISETICON", "GUISETONEVENT", "GUISETSTATE", "GUISETSTYLE", "GUISTARTGROUP", "GUISWITCH", "HEX", "HOTKEYSET", "HTTPSETPROXY", "HTTPSETUSERAGENT", "HWND", "INETCLOSE", "INETGET", "INETGETINFO", "INETGETSIZE", "INETREAD", "INIDELETE", "INIREAD", "INIREADSECTION", "INIREADSECTIONNAMES", "INIRENAMESECTION", "INIWRITE", "INIWRITESECTION", "INPUTBOX", "INT", "ISADMIN", "ISARRAY", "ISBINARY", "ISBOOL", "ISDECLARED", "ISDLLSTRUCT", "ISFLOAT", "ISFUNC", "ISHWND", "ISINT", "ISKEYWORD", "ISMAP", "ISNUMBER", "ISOBJ", "ISPTR", "ISSTRING", "LOG", "MAPAPPEND", "MAPEXISTS", "MAPKEYS", "MAPREMOVE", "MEMGETSTATS", "MOD", "MOUSECLICK", "MOUSECLICKDRAG", "MOUSEDOWN", "MOUSEGETCURSOR", "MOUSEGETPOS", "MOUSEMOVE", "MOUSEUP", "MOUSEWHEEL", "MSGBOX", "NUMBER", "OBJCREATE", "OBJCREATEINTERFACE", "OBJEVENT", "OBJGET", "OBJNAME", "ONAUTOITEXITREGISTER", "ONAUTOITEXITUNREGISTER", "OPT", "PING", "PIXELCHECKSUM", "PIXELGETCOLOR", "PIXELSEARCH", "PROCESSCLOSE", "PROCESSEXISTS", "PROCESSGETSTATS", "PROCESSLIST", "PROCESSSETPRIORITY", "PROCESSWAIT", "PROCESSWAITCLOSE", "PROGRESSOFF", "PROGRESSON", "PROGRESSSET", "PTR", "RANDOM", "REGDELETE", "REGENUMKEY", "REGENUMVAL", "REGREAD", "REGWRITE", "ROUND", "RUN", "RUNAS", "RUNASWAIT", "RUNWAIT", "SEND", "SENDKEEPACTIVE", "SETERROR", "SETEXTENDED", "SHELLEXECUTE", "SHELLEXECUTEWAIT", "SHUTDOWN", "SIN", "SLEEP", "SOUNDPLAY", "SOUNDSETWAVEVOLUME", "SPLASHIMAGEON", "SPLASHOFF", "SPLASHTEXTON", "SQRT", "SRANDOM", "STATUSBARGETTEXT", "STDERRREAD", "STDINWRITE", "STDIOCLOSE", "STDOUTREAD", "STRING", "STRINGADDCR", "STRINGCOMPARE", "STRINGFORMAT", "STRINGFROMASCIIARRAY", "STRINGINSTR", "STRINGISALNUM", "STRINGISALPHA", "STRINGISASCII", "STRINGISDIGIT", "STRINGISFLOAT", "STRINGISINT", "STRINGISLOWER", "STRINGISSPACE", "STRINGISUPPER", "STRINGISXDIGIT", "STRINGLEFT", "STRINGLEN", "STRINGLOWER", "STRINGMID", "STRINGREGEXP", "STRINGREGEXPREPLACE", "STRINGREPLACE", "STRINGREVERSE", "STRINGRIGHT", "STRINGSPLIT", "STRINGSTRIPCR", "STRINGSTRIPWS", "STRINGTOASCIIARRAY", "STRINGTOBINARY", "STRINGTRIMLEFT", "STRINGTRIMRIGHT", "STRINGUPPER", "TAN", "TCPACCEPT", "TCPCLOSESOCKET", "TCPCONNECT", "TCPLISTEN", "TCPNAMETOIP", "TCPRECV", "TCPSEND", "TCPSHUTDOWN", "TCPSTARTUP", "TIMERDIFF", "TIMERINIT", "TOOLTIP", "TRAYCREATEITEM", "TRAYCREATEMENU", "TRAYGETMSG", "TRAYITEMDELETE", "TRAYITEMGETHANDLE", "TRAYITEMGETSTATE", "TRAYITEMGETTEXT", "TRAYITEMSETONEVENT", "TRAYITEMSETSTATE", "TRAYITEMSETTEXT", "TRAYSETCLICK", "TRAYSETICON", "TRAYSETONEVENT", "TRAYSETPAUSEICON", "TRAYSETSTATE", "TRAYSETTOOLTIP", "TRAYTIP", "UBOUND", "UDPBIND", "UDPCLOSESOCKET", "UDPOPEN", "UDPRECV", "UDPSEND", "UDPSHUTDOWN", "UDPSTARTUP", "VARGETTYPE", "WINACTIVATE", "WINACTIVE", "WINCLOSE", "WINEXISTS", "WINFLASH", "WINGETCARETPOS", "WINGETCLASSLIST", "WINGETCLIENTSIZE", "WINGETHANDLE", "WINGETPOS", "WINGETPROCESS", "WINGETSTATE", "WINGETTEXT", "WINGETTITLE", "WINKILL", "WINLIST", "WINMENUSELECTITEM", "WINMINIMIZEALL", "WINMINIMIZEALLUNDO", "WINMOVE", "WINSETONTOP", "WINSETSTATE", "WINSETTITLE", "WINSETTRANS", "WINWAIT", "WINWAITACTIVE", "WINWAITCLOSE", "WINWAITNOTACTIVE" };

            for (int i = 0; i < nLines && off < RawData.Length;)
            {
                byte opCode = RawData[off++];
                if (opCode == 0x7f)
                {
                    buffer.Append(string.Join(" ", tokens.ToArray<string>()));
                    buffer.Append("\r\n");
                    tokens.Clear();
                    ++i;
                }
                else if (opCode == 0x30)
                {
                    var r = GetStr(RawData, ref off);
                    //tokens.Add(apiMap.GetValueOrDefault(r, r));
                    tokens.Add(r);
                }
                else if (opCode == 0x31)
                {
                    var r = GetStr(RawData, ref off);
                    tokens.Add(r);
                }
                else if (opCode == 0x34)
                {
                    var r = GetStr(RawData, ref off);
                    
                    //tokens.Add(apiMap.GetValueOrDefault(r, r));
                    tokens.Add(r);
                }
                else if (opCode == 0x35)
                    tokens.Add("." + GetStr(RawData, ref off));
                else if (opCode == 0x32)
                {
                    var r = "@" + GetStr(RawData, ref off);
                    //tokens.Add(apiMap.GetValueOrDefault(r, r));
                    tokens.Add(r);
                }
                else if (opCode == 0x47)
                    tokens.Add("(");
                else if (opCode == 0x48)
                    tokens.Add(")");
                else if (opCode == 0x41)
                    tokens.Add("=");
                else if (opCode == 0x42)
                    tokens.Add(">");
                else if (opCode == 0x43)
                    tokens.Add("<");
                else if (opCode == 0x44)
                    tokens.Add("<>");
                else if (opCode == 0x45)
                    tokens.Add(">=");
                else if (opCode == 0x46)
                    tokens.Add("<=");
                else if (opCode == 0x50)
                    tokens.Add("==");
                else if (opCode == 0x51)
                    tokens.Add("^");
                else if (opCode == 0x52)
                    tokens.Add("+=");
                else if (opCode == 0x53)
                    tokens.Add("-=");
                else if (opCode == 0x54)
                    tokens.Add("/=");
                else if (opCode == 0x55)
                    tokens.Add("*=");
                else if (opCode == 0x56)
                    tokens.Add("&=");
                else if (opCode == 0x57)
                    tokens.Add("?");
                else if (opCode == 0x58)
                    tokens.Add(":");
                else if (opCode == 0x4d)
                    tokens.Add("&");
                else if (opCode == 0x4e)
                    tokens.Add("[");
                else if (opCode == 0x4f)
                    tokens.Add("]");
                else if (opCode == 0x33)
                    tokens.Add("$" + GetStr(RawData, ref off));
                else if (opCode == 0x40)
                    tokens.Add(",");
                else if (opCode == 0x36)
                {
                    string first = "\"";
                    var ans = GetStr(RawData, ref off);
                    if (ans.Contains(first))
                        first = "'";
                    tokens.Add(first + ans + first);
                }
                else if (opCode == 0x05)
                {
                    tokens.Add("0x" + BitConverter.ToUInt32(RawData, off).ToString("x"));
                    off += 4;
                }
                else if (opCode == 0x10)
                {
                    tokens.Add("0x" + BitConverter.ToUInt64(RawData, off).ToString("x"));
                    off += 8;
                }
                else if (opCode == 0x20)
                {
                    tokens.Add(BitConverter.ToDouble(RawData, off).ToString());
                    off += 8;
                }
                else if (opCode == 0x37)
                    tokens.Add(GetStr(RawData, ref off));
                else if (opCode == 0x49)
                    tokens.Add("+");
                else if (opCode == 0x4a)
                    tokens.Add("-");
                else if (opCode == 0x4b)
                    tokens.Add("/");
                else if (opCode == 0x4c)
                    tokens.Add("*");
                else if (opCode == 0)
                {
                    // keyword
                    int index = BitConverter.ToInt32(RawData, off);
                    off += 4;
                    tokens.Add(KEYWORDS[index]);
                }
                else if (opCode == 1)
                {
                    // function
                    int index = BitConverter.ToInt32(RawData, off);
                    off += 4;
                    tokens.Add(FUNCTIONS[index]);
                }
                //else if (opCode < 5)
                //{
                //    tokens.Add(string.Format("token_type[{1} -> 0x{0}]", BitConverter.ToUInt32(RawData, off).ToString("x"), opCode));
                //    off += 4;
                //}
            }

            if (tokens.Count > 0)
            {
                buffer.Append(string.Join(" ", tokens.ToArray<string>()));
                buffer.Append("\r\n");
                tokens.Clear();
            }

            Type = Utils.TYPE.Text;
            State = "Decompiled";
            Status = Utils.STATUS.OK;
            SourceState = Utils.SOURCE_STATE.Decompiled;
            SourceCode = buffer.ToString();
            RawData = Encoding.ASCII.GetBytes(SourceCode);
            RawDataSize = (uint)buffer.Length;
            DoUpdate();
        }

        private string GetStr(byte[] buf, ref int pos)
        {
            int len = BitConverter.ToInt32(buf, pos);
            pos += 4;
            byte[] temp = new byte[len];
            for (int i = 0; i < len * 2; i += 2)
                temp[i / 2] = (byte)(buf[pos + i] ^ len);
            pos += len * 2;
            return Encoding.ASCII.GetString(temp);
        }

        private static string Join(string a, string b) => System.IO.Path.Combine(a, b);

        internal unsafe void Tidy(MainForm sender)
        {
            //new Thread(() => Decompile(form)).Start();
            Decompile();
            //evtDecompiled.WaitOne();

            if (Status != Utils.STATUS.OK)
            {
                //evtIndented.Set();
                //evtNotify.Set();
                MarkComplete();
                return;
            }
            //form.SetText("Indenting Code...", form.lblStatus);
            State = "Indenting Code...";
            DoUpdate();
            var res = Main.Tidy;

            int cavePos = Utils.Find(res, new byte[] { 0xdd, 0xcc, 0xbb, 0xaa, 0xdd, 0xcc, 0xbb, 0xaa });
            if (cavePos == -1)
            {
                //evtIndented.Set();
                MarkComplete();
                return;
            }
            var sName = Join(GetTempDir(), "aut" + (new Random().Next(0xdead, 0xfeed)) + ".au3");
            var strm = new System.IO.StreamWriter(sName);
            strm.WriteLine(SourceCode);
            strm.Close();

            var sz = Encoding.ASCII.GetBytes(sName + "\x00");
            Array.Copy(sz, 0, res, cavePos, sz.Length);

            var fName = Join(GetTempDir(), "aut" + (new Random().Next(0xdead, 0xfeed)) + ".exe");
            var stream = new System.IO.FileStream(fName, System.IO.FileMode.Create, System.IO.FileAccess.Write);
            stream.Write(res, 0, res.Length);
            stream.Close();

            var tidyOpts = new System.IO.StreamWriter(Join(GetTempDir(), "tidy.ini"));
            tidyOpts.WriteLine("[ProgramSettings]");
            tidyOpts.WriteLine("tabchar=4");
            tidyOpts.WriteLine("tabsize=4");
            tidyOpts.WriteLine("proper=1");
            tidyOpts.WriteLine("properconstants=1");
            tidyOpts.WriteLine("delim=1");
            tidyOpts.WriteLine("vars=2");
            tidyOpts.WriteLine("Tidy_commentblock=1");
            tidyOpts.WriteLine("End_With_NewLine=1");
            //tidyOpts.WriteLine("ShowConsoleInfo=9");
            tidyOpts.Close();

            var au3API = Join(GetTempDir(), "au3.api");
            System.IO.File.WriteAllBytes(au3API, Main.au3);

            var funcAPI = Join(GetTempDir(), "functions.tbl");
            System.IO.File.WriteAllBytes(funcAPI, Main.functions);

            var macroAPI = Join(GetTempDir(), "macros.tbl");
            System.IO.File.WriteAllBytes(macroAPI, Main.macros);

            var keywordAPI = Join(GetTempDir(), "keywords.tbl");
            System.IO.File.WriteAllBytes(keywordAPI, Main.keywords);

            var process = new System.Diagnostics.Process();

            sender.Quit += (e, evArgs) =>
            {
                if (! process.HasExited)
                {
                    process.Kill();
                }
                process.WaitForExit();
                try { System.IO.File.Delete(fName); } catch (Exception) { }
                try { System.IO.File.Delete(Join(GetTempDir(), "tidy.ini")); } catch (Exception) { }
                try { System.IO.File.Delete(au3API); } catch (Exception) { }
                try { System.IO.File.Delete(funcAPI); } catch (Exception) { }
                try { System.IO.File.Delete(macroAPI); } catch (Exception) { }
                try { System.IO.File.Delete(keywordAPI); } catch (Exception) { }
                try { System.IO.File.Delete(sName); } catch (Exception) { }
                var dir0 = Join(GetTempDir(), "BackUp");
                if (System.IO.Directory.Exists(dir0))
                    System.IO.Directory.Delete(dir0, true);
            };

            process.StartInfo.FileName = fName;
            process.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            process.StartInfo.UseShellExecute = true;
            process.Start();

            process.WaitForExit();

            try { System.IO.File.Delete(fName); } catch (Exception) { }
            try { System.IO.File.Delete(Join(GetTempDir(), "tidy.ini")); } catch (Exception) { }
            try { System.IO.File.Delete(au3API); } catch (Exception) { }
            try { System.IO.File.Delete(funcAPI); } catch (Exception) { }
            try { System.IO.File.Delete(macroAPI); } catch (Exception) { }
            try { System.IO.File.Delete(keywordAPI); } catch (Exception) { }
            var src = System.IO.File.ReadAllText(sName).Trim('\r', '\n');
            if (src.Length > 0)
            {
                SourceCode = src;
                RawData = Encoding.ASCII.GetBytes(src);
                RawDataSize = (uint)src.Length;
            }
            try { System.IO.File.Delete(sName); } catch (Exception) { }

            var dir = Join(GetTempDir(), "BackUp");
            if (System.IO.Directory.Exists(dir))
                System.IO.Directory.Delete(dir, true);
            
            SourceState = Utils.SOURCE_STATE.Indented;
            //form.SetText("Code Indented.", form.lblStatus);
            State = "Code Indented.";
            Status = Utils.STATUS.OK;
            MarkComplete();
        }

        static string GetTempDir()
        {
            var buf = new StringBuilder(260);
            GetTempPath(260, buf);
            return buf.ToString();
        }

        [DllImport("kernel32.dll")]
        static extern uint GetTempPath(uint nBufferLength, [Out] StringBuilder lpBuffer);

    }
}
