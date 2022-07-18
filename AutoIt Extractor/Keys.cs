using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoIt_Extractor
{
    internal class Legacy : EA05
    {
        public override void Decompress(AU3_Resource res)
        {
            res.State = "Extracted";
            res.DoUpdate();
            res.count = 0;
            //form.SetText("Decompressing...", form.lblStatus);
            if (res.CompressedSize == res.DecompressedSize)
            {
                //form.SetText("Not Compressed.", form.lblStatus);
                //res.evtDecompressed.Set();
                return;
            }
            res.pMem = new byte[res.DecompressedSize];
            res.spos = 0;
            res.pos = 8;    // "EA06/EA05/JB??", p32(size)
            if (!Utils.AU3_SIGN.Contains(BitConverter.ToUInt32(res.RawData, 0)))
            {
                res.Status = Utils.STATUS.InvalidCompressedHeader;
                res.State = "Invalid Compressed File Format!";
                res.DoUpdate();
                //res.evtDecompressed.Set();
                return; // invalid signature
            }

            if (BitConverter.ToUInt32(res.RawData, 0) == 0x3130424a)
            {
                try
                {
                    res.JB01_Decompress(this);
                } catch (Exception)
                {
                    res.count = 0;
                    res.pos = 8;
                    res.DecompressorInitLegacy();

                    while (res.spos < res.DecompressedSize)
                    {
                        res.r = res.sub_417518(this);
                        if (res.r < 0x100)
                        {
                            res.pMem[res.spos++] = (byte)res.r;
                        }
                        else
                        {
                            res.v = res.sub_417665(res.r, this) + 3;
                            res.delta = res.spos - res.sub_4176a5(this);
                            while (res.spos < res.pMem.Length && res.v-- > 0)
                                res.pMem[res.spos++] = res.pMem[res.delta++];
                        }
                    }
                }
            }
            else
            {
                res.count = 0;
                res.pos = 8;
                res.DecompressorInitLegacy();

                while (res.spos < res.DecompressedSize)
                {
                    res.r = res.sub_417518(this);
                    if (res.r < 0x100)
                    {
                        res.pMem[res.spos++] = (byte)res.r;
                    }
                    else
                    {
                        res.v = res.sub_417665(res.r, this) + 3;
                        res.delta = res.spos - res.sub_4176a5(this);
                        while (res.spos < res.pMem.Length && res.v-- > 0)
                            res.pMem[res.spos++] = res.pMem[res.delta++];
                    }
                }
            }
            res.RawDataSize = res.DecompressedSize;
            res.RawData = res.pMem;
            if (this.IsUnicode)
                res.SourceCode = Encoding.Unicode.GetString(res.pMem);
            else
                res.SourceCode = Encoding.ASCII.GetString(res.pMem);
            res.Status = Utils.STATUS.OK;
            res.State = "Decompressed";
            res.Type = Utils.TYPE.Text;
            res.SourceState = Utils.SOURCE_STATE.Decompressed;
            //form.SetText("Decompressed.", form.lblStatus);
            //res.evtDecompressed.Set();
            //form.UpdateStatus(null, null);

            if (! res.Tag.Contains("SCRIPT"))
            {
                res.MarkComplete();
            }
            else
            {
                res.DoUpdate();
            }
        }

        internal class Rand2
        {
            private uint seed;

            internal Rand2(uint seed)
            {
                this.seed = seed;
            }

            internal uint Rand()
            {
                seed = seed * 0x343fd + 0x269ec3;
                return seed >> 16 & 0x7fff;
            }
        }

        public override unsafe void ShittyEncoder(byte[] buffer, int key, bool add=true, bool useOldRand=false)
        {
            int k = key;
            if (add)
            {
                k += buffer.Length;
            }
            else
            {
                // add 0x849 which is sum(map(ord, md5('').digest()))
                k += 0x849;
            }
            if (useOldRand)
            {
                var rnd = new Rand2((uint)k);
                for (int i = 0; i < buffer.Length; ++i)
                    buffer[i] ^= (byte)rnd.Rand();
            }
            else
            {
                Random.SRand(k);
                for (int i = 0; i < buffer.Length; ++i)
                    buffer[i] ^= Random.Next();
            }
        }
    }

    internal class EA05 : Keys
    {
        public EA05()
        {
            TagSize = 0x29bc;   // FILE: 0x16fa
            Tag = 0xa25e;
            PathSize = 0x29ac;
            Path = 0xf25e;
            CompressedSize = 0x45aa;
            DecompressedSize = 0x45aa;
            Checksum = 0xc3d2;
            Data = 0x22af;
            IsUnicode = false;
        }

        public override unsafe void ShittyEncoder(byte[] buffer, int key, bool add=true, bool useOldRand=false)
        {
            int k = key;
            if (add)
            {
                k += buffer.Length;
            }
            else
            {
                // add 0x849 which is sum(map(ord, md5('').digest()))
                k += 0x849;
            }
            Random.SRand(k);
            for (int i = 0; i < buffer.Length; ++i)
                buffer[i] ^= Random.Next();
        }

        internal class Random
        {
            static RandState state;
            public static void SRand(int key)
            {
                state.nums = new uint[624];
                state.nums[0] = (uint)key;
                for (int i = 1; i < 624; ++i)
                {
                    state.nums[i] = (uint) ((state.nums[i - 1] >> 30 ^ state.nums[i - 1]) * 0x6C078965 + i);
                }
                state.A = 1;
                state.B = 1;
            }

            //[StructLayout(LayoutKind.Sequential)]
            private struct RandState
            {
                internal uint[] nums;
                internal int A;
                internal int B;
                internal int next;
            }

            public static byte Next()
            {
                if (--state.A == 0)
                {
                    InternalShit();
                }
                uint ecx = state.nums[state.next++];
                ecx = ecx ^ ecx >> 11;
                var eax = (ecx & 0xFF3A58AD) << 7;
                ecx ^= eax;
                eax = (ecx & 0xFFFFDF8C) << 0xf;
                ecx ^= eax;
                eax = ecx ^ ecx >> 0x12;
                return (byte)(eax >> 1);
            }

            private static void InternalShit()
            {
                state.A = 0x270;
                state.next = 0;
                for (int i = 0; i < 0xe3; ++i)
                {
                    var edx = state.nums[i] ^ state.nums[i + 1];
                    edx &= 0x7FFFFFFE;
                    edx ^= state.nums[i];
                    edx >>= 1;
                    var ecx = 0x9908B0DF;
                    if (state.nums[i + 1] % 2 == 0)
                        ecx = 0;
                    edx ^= ecx;
                    edx ^= state.nums[i + 397];
                    state.nums[i] = edx;
                }
                for (int i = 0xe3; i < 0x18c+0xe3; ++i)
                {
                    var edx = state.nums[i] ^ state.nums[i + 1];
                    edx &= 0x7FFFFFFE;
                    edx ^= state.nums[i];
                    edx >>= 1;
                    var ecx = 0x9908B0DF;
                    if (state.nums[i + 1] % 2 == 0)
                        ecx = 0;
                    edx ^= ecx;
                    edx ^= state.nums[i - 227];
                    state.nums[i] = edx;
                }
                var ebx = state.nums[0];
                var edx2 = state.nums[0x18c+0xe3] ^ ebx;
                edx2 &= 0x7FFFFFFE;
                edx2 ^= state.nums[0x18c+0xe3];
                edx2 >>= 1;
                if (ebx % 2 == 1)
                    ebx = 0x9908B0DF;
                else
                    ebx = 0;
                edx2 ^= ebx;
                edx2 ^= state.nums[0x18c+0xe3-227];
                state.nums[0x18c+0xe3] = edx2;
            }
        }
        public override void Decompress(AU3_Resource res)
        {
            res.State = "Extracted";
            res.count = 0;
            res.DoUpdate();
            //form.SetText("Decompressing...", form.lblStatus);
            if (res.CompressedSize == res.DecompressedSize)
            {
                //form.SetText("Not Compressed.", form.lblStatus);
                //res.evtDecompressed.Set();
                return;
            }
            var mem = new byte[res.DecompressedSize];
            int i = 0;
            res.pos = 8;    // "EA06/EA05/JB??", p32(size)
            if (!Utils.AU3_SIGN.Contains(BitConverter.ToUInt32(res.RawData, 0)))
            {
                res.Status = Utils.STATUS.InvalidCompressedHeader;
                res.State = "Invalid Compressed File Format!";
                //res.evtDecompressed.Set();
                res.DoUpdate();
                return; // invalid signature
            }

            while (i < res.DecompressedSize)
            {
                var r = ExtractBits(res, 1);
                if (r == 0)
                {
                    mem[i++] = (byte)ExtractBits(res, 8);
                }
                else
                {
                    var v = ExtractBits(res, 0xf);
                    r = CustomExtractBits(res);
                    var delta = i - v;
                    while (r-- > 0)
                        mem[i++] = mem[delta++];
                }
            }
            res.RawDataSize = res.DecompressedSize;
            res.RawData = mem;
            if (res.Tag.Contains("UNICODE"))
                res.SourceCode = Encoding.Unicode.GetString(mem, 2, mem.Length - 2);
            else
                res.SourceCode = Encoding.ASCII.GetString(mem);
            res.Status = Utils.STATUS.OK;
            res.State = "Decompressed";
            res.Type = Utils.TYPE.Text;
            res.SourceState = Utils.SOURCE_STATE.Decompressed;
            //form.SetText("Decompressed.", form.lblStatus);
            //res.evtDecompressed.Set();
            //form.UpdateStatus(null, null);
            if (! res.Tag.Contains("SCRIPT<"))
            {
                res.MarkComplete();
            }
            else
            {
                res.DoUpdate();
            }
        }

    }

    internal class EA06 : Keys
    {
        public EA06()
        {
            TagSize = 0xadbc;
            Tag = 0xb33f;
            PathSize = 0xf820;
            Path = 0xf479;
            CompressedSize = 0x87bc;
            DecompressedSize = 0x87bc;
            Checksum = 0xa685;
            Data = 0x2477;
            IsUnicode = true;
        }

        public override unsafe void ShittyEncoder(byte[] buffer, int key, bool add=true, bool useOldRand = false)
        {
            Random.Init0();
            int k = key;
            if (add) k += buffer.Length/2;
            Random.Init1(k);
            //byte[] buffer = new byte[buf.Length];
            //Array.Copy(buf, buffer, buf.Length);
            for (int i = 0; i < buffer.Length; ++i)
                buffer[i] ^= Random.Next();
        }

        private class Random
        {
            private static AU3_RandState state;
            [StructLayout(LayoutKind.Sequential)]
            private unsafe struct AU3_RandState
            {
                internal int A;
                internal int B;
                internal fixed int nums[17];
                internal fixed int nums2[17];
                internal fixed int nums3[17];
                internal int C;
            }

            private static int Rotl(int v, int nBits)
            {
                uint val = (uint)v;
                return (int)(val << nBits | val >> 32 - nBits);
            }

            private unsafe static double FPUStuff()
            {
                int val = Rotl(state.nums[state.A], 9) + Rotl(state.nums[state.B], 13);
                state.nums[state.A] = val;
                if (--state.A < 0)
                    state.A = 0x10;
                if (--state.B < 0)
                    state.B = 0x10;
                if (state.nums[state.A] == state.nums2[0])
                {
                    int[] temp = new int[0x11];
                    int[] temp1 = new int[0x11];
                    fixed (AU3_RandState* p = &state)
                    {
                        int* f = (int*)p;
                        Marshal.Copy((IntPtr)(p + 0x24 - state.A), temp, 0, 0x11);
                    }
                    fixed (int* p = state.nums)
                    {
                        Marshal.Copy((IntPtr)p, temp1, 0, 0x11);
                    }
                    if (temp.SequenceEqual(temp1))
                        return 0;
                }
                if (state.C == 0)
                {
                    double ret = 0;
                    uint* p = (uint*)&ret;
                    uint v = (uint)val;
                    p[0] = v << 0x14;
                    p[1] = v >> 12 | 0x3FF00000;
                    return ret - 1.0;
                }
                else if (state.C == 1)
                {
                    double ret = 0;
                    uint* p = (uint*)&ret;
                    uint v = (uint)val;
                    p[1] = v << 0x14;
                    p[0] = v >> 12 | 0x3FF00000;
                    return ret - 1.0;
                }
                else
                {
                    double ret = val;
                    if (val < 0)
                        ret += 4294967296.0;
                    return ret * 2.328306436538696e-10;
                }
            }

            internal unsafe static void Init1(int time)
            {
                for (int i = 0; i < 0x11; ++i)
                {
                    time = 1 - time * 0x53a9b4fb;
                    state.nums[i] = time;
                }
                state.A = 0;
                state.B = 10;
                for (int i = 0; i < 0x11; ++i)
                {
                    state.nums2[i] = state.nums[i];
                    state.nums3[i] = state.nums[i];
                }
                for (int i = 0; i < 9; ++i)
                    FPUStuff();
            }

            internal unsafe static void Init0()
            {
                //var stamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
                //var stamp = Utils.Time64(IntPtr.Zero);
                var stamp = 0xdeadc0de;
                Init1((int)stamp);
                double d = 1.0;
                int* p = (int*)&d;
                if (p[1] == 0x3FF00000)
                {
                    state.C = 0;
                }
                else
                {
                    state.C = 1;
                    if (p[0] != 0x3FF00000)
                        state.C = 2;
                }
            }

            public unsafe static byte Next()
            {
                FPUStuff();
                int ret = (int)(256.0 * FPUStuff());
                if (ret < 0x100)
                    return (byte)ret;
                return 0xff;
            }
        }

        public override void Decompress(AU3_Resource res)
        {
            res.State = "Extracted";
            res.count = 0;
            res.DoUpdate();
            //form.SetText("Decompressing...", form.lblStatus);
            if (res.CompressedSize == res.DecompressedSize)
            {
                //form.SetText("Not Compressed.", form.lblStatus);
                //res.evtNotify.Set();
                if (! res.Tag.Contains("SCRIPT<"))
                {
                    res.MarkComplete();
                }
                else
                {
                    res.DoUpdate();
                }
                return;
            }
            var mem = new byte[res.DecompressedSize];
            int i = 0;
            res.pos = 8;    // "EA06", p32(size)
            if (!Utils.AU3_SIGN.Contains(BitConverter.ToUInt32(res.RawData, 0)))
            {
                res.Status = Utils.STATUS.InvalidCompressedHeader;
                res.State = "Invalid Compressed File Format!";
                //res.evtNotify.Set();
                return; // invalid signature
            }

            while (i < res.DecompressedSize)
            {
                var r = ExtractBits(res, 1);
                if (r == 1)
                {
                    mem[i++] = (byte)ExtractBits(res, 8);
                }
                else
                {
                    var v = ExtractBits(res, 0xf);
                    r = CustomExtractBits(res);
                    var delta = i - v;
                    while (r-- > 0)
                        mem[i++] = mem[delta++];
                }
            }
            res.RawDataSize = res.DecompressedSize;
            res.RawData = mem;
            res.Status = Utils.STATUS.OK;
            res.State = "Decompressed";
            res.Type = Utils.TYPE.Binary;
            res.SourceState = Utils.SOURCE_STATE.Decompressed;
            //form.SetText("Decompressed.", form.lblStatus);
            //res.evtNotify.Set();
            //form.UpdateStatus(null, null);
            if (!res.Tag.Contains("SCRIPT<"))
            {
                res.MarkComplete();
            }
            else
            {
                res.DoUpdate();
            }
        }
    }
    internal abstract class Keys
    {
        internal int TagSize, Tag;
        internal int Path, PathSize;
        internal int CompressedSize;
        internal int DecompressedSize;
        internal int Checksum;
        internal int Data;
        internal bool IsUnicode;

        public unsafe uint CustomExtractBits(AU3_Resource res)
        {
            uint ret = 0;
            var r = ExtractBits(res, 2);
            if (r == 3)
            {
                ret = 3;
                r = ExtractBits(res, 3);
                if (r == 7)
                {
                    ret = 10;
                    r = ExtractBits(res, 5);
                    if (r == 0x1f)
                    {
                        ret = 0x29;
                        while ((r = ExtractBits(res, 8)) == 0xff)
                            ret += 0xff;
                    }
                }
            }
            return ret + r + 3;
        }

        public unsafe uint ExtractBits(AU3_Resource res, int nBits)
        {
            res.ans &= 0xffff;
            while (nBits-- > 0)
            {
                if (res.count == 0)
                {
                    uint temp = NextByte(res);
                    res.ans |= temp << 8 | NextByte(res);
                    res.count = 0x10;
                }
                res.ans <<= 1;
                res.count--;
            }
            return res.ans >> 0x10;
        }
        //abstract public unsafe uint CustomExtractBits(AU3_Resource res);
        abstract public unsafe void ShittyEncoder(byte[] buffer, int key, bool add=true, bool useOldRand = false);
        public abstract void Decompress(AU3_Resource res);

        public unsafe string DecodeString(byte[] script, int start, int size, int key, bool add=true, bool useOldRand = false)
        {
            byte[] buf = new byte[size];
            Array.Copy(script, start, buf, 0, size);
            ShittyEncoder(buf, key, add, useOldRand);
            if (IsUnicode)
                return Encoding.Unicode.GetString(buf);
            else
                return Encoding.ASCII.GetString(buf);
        }

        internal byte NextByte(AU3_Resource res)
        {
            return res.RawData[res.pos++];
        }
    }

    internal class KeyFactory
    {
        public static Keys GetKeys(string subtype)
        {
            if (subtype == "AU3!EA06")
            {
                return new EA06();
            }
            else if (subtype == "AU3!EA05")
            {
                return new EA05();
            }
            else if (subtype == "AU3!OLD")
            {
                return new Legacy();
            }
            return null;
        }
    }
}
