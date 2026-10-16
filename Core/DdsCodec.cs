using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace YTDEditor.Core
{
    public class DdsMetadata
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int MipCount { get; set; }
        public string FourCC { get; set; }
        public uint DxgiFormat { get; set; }
        public bool IsSrgb { get; set; }
        public bool IsDxt5 { get; set; }
        public int DataOffset { get; set; }
        public byte[] RawHeader { get; set; }
        public string FormatName { get; set; }
    }

    public class DdsEncodeStats
    {
        public string ToolUsed { get; set; }
        public bool BlockPatched { get; set; }
        public int TouchedBlocks { get; set; }
        public int TotalBlocks { get; set; }
    }

    public class DdsCodec
    {
        private readonly AppConfig _config;

        public DdsCodec(AppConfig config)
        {
            _config = config;
        }

        public static DdsMetadata ParseDdsHeader(byte[] ddsBytes)
        {
            if (ddsBytes == null || ddsBytes.Length < 128 ||
                ddsBytes[0] != (byte)'D' || ddsBytes[1] != (byte)'D' || ddsBytes[2] != (byte)'S' || ddsBytes[3] != (byte)' ')
            {
                throw new InvalidDataException("Geçersiz DDS başlığı (DDS magic bulunamadı).");
            }

            int height = BitConverter.ToInt32(ddsBytes, 12);
            int width = BitConverter.ToInt32(ddsBytes, 16);
            int mipCount = Math.Max(1, BitConverter.ToInt32(ddsBytes, 28));
            uint pfFlags = BitConverter.ToUInt32(ddsBytes, 80);
            string fourCC = Encoding.ASCII.GetString(ddsBytes, 84, 4).Trim('\0');

            bool hasFourCC = (pfFlags & 0x4) != 0;
            int dataOffset = 128;
            uint dxgiFormat = 0;
            bool isSrgb = false;
            bool isDxt5 = false;
            string formatName = hasFourCC ? fourCC : "RGBA8";

            if (hasFourCC && fourCC == "DX10")
            {
                if (ddsBytes.Length < 148)
                    throw new InvalidDataException("DX10 başlığı eksik.");
                dxgiFormat = BitConverter.ToUInt32(ddsBytes, 128);
                dataOffset = 148;
                if (dxgiFormat == 77 || dxgiFormat == 78)
                {
                    isDxt5 = true;
                    isSrgb = (dxgiFormat == 78);
                    formatName = isSrgb ? "BC3_UNORM_SRGB" : "BC3_UNORM";
                }
                else if (dxgiFormat == 71 || dxgiFormat == 72)
                {
                    isSrgb = (dxgiFormat == 72);
                    formatName = isSrgb ? "BC1_UNORM_SRGB" : "BC1_UNORM";
                }
                else if (dxgiFormat == 28 || dxgiFormat == 29)
                {
                    isSrgb = (dxgiFormat == 29);
                    formatName = "R8G8B8A8_UNORM";
                }
                else
                {
                    formatName = "DXGI_" + dxgiFormat;
                }
            }
            else if (hasFourCC && (fourCC == "DXT5" || fourCC == "BC3U"))
            {
                isDxt5 = true;
                formatName = "DXT5";
            }
            else if (hasFourCC && (fourCC == "DXT1" || fourCC == "BC1U"))
            {
                formatName = "DXT1";
            }

            byte[] rawHeader = new byte[dataOffset];
            Buffer.BlockCopy(ddsBytes, 0, rawHeader, 0, dataOffset);

            return new DdsMetadata
            {
                Width = width,
                Height = height,
                MipCount = mipCount,
                FourCC = fourCC,
                DxgiFormat = dxgiFormat,
                IsSrgb = isSrgb,
                IsDxt5 = isDxt5,
                DataOffset = dataOffset,
                RawHeader = rawHeader,
                FormatName = formatName
            };
        }

        public static byte[] BuildStandardDxt5Header(int width, int height, int mipCount)
        {
            int bx = Math.Max(1, (width + 3) / 4);
            int by = Math.Max(1, (height + 3) / 4);
            int linearSize = bx * by * 16;

            uint flags = 0x1u | 0x2u | 0x4u | 0x1000u | 0x80000u;
            if (mipCount > 1) flags |= 0x20000u;

            uint caps = 0x1000u;
            if (mipCount > 1) caps |= 0x8u | 0x400000u;

            byte[] hdr = new byte[128];
            hdr[0] = (byte)'D'; hdr[1] = (byte)'D'; hdr[2] = (byte)'S'; hdr[3] = (byte)' ';
            Buffer.BlockCopy(BitConverter.GetBytes(124u), 0, hdr, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(flags), 0, hdr, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(height), 0, hdr, 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(width), 0, hdr, 16, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(linearSize), 0, hdr, 20, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(mipCount), 0, hdr, 28, 4);

            // PixelFormat at offset 76
            Buffer.BlockCopy(BitConverter.GetBytes(32u), 0, hdr, 76, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0x4u), 0, hdr, 80, 4); // DDPF_FOURCC
            hdr[84] = (byte)'D'; hdr[85] = (byte)'X'; hdr[86] = (byte)'T'; hdr[87] = (byte)'5';

            // Caps at offset 108
            Buffer.BlockCopy(BitConverter.GetBytes(caps), 0, hdr, 108, 4);
            return hdr;
        }

        public static byte[] BuildUncompressedRgba8Dds(byte[] rgba, int width, int height)
        {
            int pitch = width * 4;
            uint flags = 0x1u | 0x2u | 0x4u | 0x1000u | 0x8u;
            uint pfFlags = 0x40u | 0x1u; // DDPF_RGB | DDPF_ALPHAPIXELS

            byte[] outBuf = new byte[128 + rgba.Length];
            outBuf[0] = (byte)'D'; outBuf[1] = (byte)'D'; outBuf[2] = (byte)'S'; outBuf[3] = (byte)' ';
            Buffer.BlockCopy(BitConverter.GetBytes(124u), 0, outBuf, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(flags), 0, outBuf, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(height), 0, outBuf, 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(width), 0, outBuf, 16, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(pitch), 0, outBuf, 20, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, outBuf, 28, 4);

            Buffer.BlockCopy(BitConverter.GetBytes(32u), 0, outBuf, 76, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(pfFlags), 0, outBuf, 80, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(32u), 0, outBuf, 88, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0x000000FFu), 0, outBuf, 92, 4); // R
            Buffer.BlockCopy(BitConverter.GetBytes(0x0000FF00u), 0, outBuf, 96, 4); // G
            Buffer.BlockCopy(BitConverter.GetBytes(0x00FF0000u), 0, outBuf, 100, 4); // B
            Buffer.BlockCopy(BitConverter.GetBytes(0xFF000000u), 0, outBuf, 104, 4); // A
            Buffer.BlockCopy(BitConverter.GetBytes(0x1000u), 0, outBuf, 108, 4);

            Buffer.BlockCopy(rgba, 0, outBuf, 128, rgba.Length);
            return outBuf;
        }

        public static byte[] NormalizeDx10Bc3ToDxt5(byte[] ddsBytes)
        {
            var meta = ParseDdsHeader(ddsBytes);
            if (meta.DataOffset == 148 && meta.IsDxt5)
            {
                byte[] stdHdr = BuildStandardDxt5Header(meta.Width, meta.Height, meta.MipCount);
                byte[] payload = new byte[stdHdr.Length + (ddsBytes.Length - 148)];
                Buffer.BlockCopy(stdHdr, 0, payload, 0, stdHdr.Length);
                Buffer.BlockCopy(ddsBytes, 148, payload, stdHdr.Length, ddsBytes.Length - 148);
                return payload;
            }
            return ddsBytes;
        }

        public static byte[] DecodeDxt5ToRgba(byte[] dds, int width, int height, int offset)
        {
            int bx = Math.Max(1, (width + 3) / 4);
            int by = Math.Max(1, (height + 3) / 4);
            byte[] rgba = new byte[width * height * 4];

            int pos = offset;
            int[] alphaPalette = new int[8];
            int[] rPal = new int[4];
            int[] gPal = new int[4];
            int[] bPal = new int[4];

            for (int yBlock = 0; yBlock < by; yBlock++)
            {
                for (int xBlock = 0; xBlock < bx; xBlock++)
                {
                    if (pos + 16 > dds.Length) break;

                    int a0 = dds[pos];
                    int a1 = dds[pos + 1];
                    alphaPalette[0] = a0;
                    alphaPalette[1] = a1;
                    if (a0 > a1)
                    {
                        for (int j = 1; j <= 6; j++)
                            alphaPalette[j + 1] = ((7 - j) * a0 + j * a1 + 3) / 7;
                    }
                    else
                    {
                        for (int j = 1; j <= 4; j++)
                            alphaPalette[j + 1] = ((5 - j) * a0 + j * a1 + 2) / 5;
                        alphaPalette[6] = 0;
                        alphaPalette[7] = 255;
                    }

                    ulong alphaBits = 0;
                    for (int i = 0; i < 6; i++)
                        alphaBits |= ((ulong)dds[pos + 2 + i]) << (8 * i);

                    ushort c0 = (ushort)(dds[pos + 8] | (dds[pos + 9] << 8));
                    ushort c1 = (ushort)(dds[pos + 10] | (dds[pos + 11] << 8));

                    UnpackRgb565(c0, out rPal[0], out gPal[0], out bPal[0]);
                    UnpackRgb565(c1, out rPal[1], out gPal[1], out bPal[1]);

                    rPal[2] = (2 * rPal[0] + rPal[1] + 1) / 3;
                    gPal[2] = (2 * gPal[0] + gPal[1] + 1) / 3;
                    bPal[2] = (2 * bPal[0] + bPal[1] + 1) / 3;

                    rPal[3] = (rPal[0] + 2 * rPal[1] + 1) / 3;
                    gPal[3] = (gPal[0] + 2 * gPal[1] + 1) / 3;
                    bPal[3] = (bPal[0] + 2 * bPal[1] + 1) / 3;

                    uint colorBits = BitConverter.ToUInt32(dds, pos + 12);
                    pos += 16;

                    for (int py = 0; py < 4; py++)
                    {
                        int iy = yBlock * 4 + py;
                        if (iy >= height) continue;
                        for (int px = 0; px < 4; px++)
                        {
                            int ix = xBlock * 4 + px;
                            if (ix >= width) continue;

                            int pIdx = py * 4 + px;
                            int aIdx = (int)((alphaBits >> (3 * pIdx)) & 0x7UL);
                            int cIdx = (int)((colorBits >> (2 * pIdx)) & 0x3U);

                            int dst = (iy * width + ix) * 4;
                            rgba[dst] = (byte)rPal[cIdx];
                            rgba[dst + 1] = (byte)gPal[cIdx];
                            rgba[dst + 2] = (byte)bPal[cIdx];
                            rgba[dst + 3] = (byte)alphaPalette[aIdx];
                        }
                    }
                }
            }
            return rgba;
        }

        public static byte[] DecodeDxt1ToRgba(byte[] dds, int width, int height, int offset)
        {
            int bx = Math.Max(1, (width + 3) / 4);
            int by = Math.Max(1, (height + 3) / 4);
            byte[] rgba = new byte[width * height * 4];

            int pos = offset;
            int[] rPal = new int[4];
            int[] gPal = new int[4];
            int[] bPal = new int[4];
            int[] aPal = new int[4];

            for (int yBlock = 0; yBlock < by; yBlock++)
            {
                for (int xBlock = 0; xBlock < bx; xBlock++)
                {
                    if (pos + 8 > dds.Length) break;
                    ushort c0 = (ushort)(dds[pos] | (dds[pos + 1] << 8));
                    ushort c1 = (ushort)(dds[pos + 2] | (dds[pos + 3] << 8));
                    UnpackRgb565(c0, out rPal[0], out gPal[0], out bPal[0]);
                    UnpackRgb565(c1, out rPal[1], out gPal[1], out bPal[1]);
                    aPal[0] = 255; aPal[1] = 255; aPal[2] = 255; aPal[3] = 255;

                    if (c0 > c1)
                    {
                        rPal[2] = (2 * rPal[0] + rPal[1] + 1) / 3;
                        gPal[2] = (2 * gPal[0] + gPal[1] + 1) / 3;
                        bPal[2] = (2 * bPal[0] + bPal[1] + 1) / 3;
                        rPal[3] = (rPal[0] + 2 * rPal[1] + 1) / 3;
                        gPal[3] = (gPal[0] + 2 * gPal[1] + 1) / 3;
                        bPal[3] = (bPal[0] + 2 * bPal[1] + 1) / 3;
                    }
                    else
                    {
                        rPal[2] = (rPal[0] + rPal[1]) / 2;
                        gPal[2] = (gPal[0] + gPal[1]) / 2;
                        bPal[2] = (bPal[0] + bPal[1]) / 2;
                        rPal[3] = 0; gPal[3] = 0; bPal[3] = 0; aPal[3] = 0;
                    }

                    uint colorBits = BitConverter.ToUInt32(dds, pos + 4);
                    pos += 8;

                    for (int py = 0; py < 4; py++)
                    {
                        int iy = yBlock * 4 + py;
                        if (iy >= height) continue;
                        for (int px = 0; px < 4; px++)
                        {
                            int ix = xBlock * 4 + px;
                            if (ix >= width) continue;
                            int pIdx = py * 4 + px;
                            int cIdx = (int)((colorBits >> (2 * pIdx)) & 0x3U);
                            int dst = (iy * width + ix) * 4;
                            rgba[dst] = (byte)rPal[cIdx];
                            rgba[dst + 1] = (byte)gPal[cIdx];
                            rgba[dst + 2] = (byte)bPal[cIdx];
                            rgba[dst + 3] = (byte)aPal[cIdx];
                        }
                    }
                }
            }
            return rgba;
        }

        private static void UnpackRgb565(ushort c, out int r, out int g, out int b)
        {
            int r5 = (c >> 11) & 0x1F;
            int g6 = (c >> 5) & 0x3F;
            int b5 = c & 0x1F;
            r = (r5 << 3) | (r5 >> 2);
            g = (g6 << 2) | (g6 >> 4);
            b = (b5 << 3) | (b5 >> 2);
        }

        public byte[] DecodeDdsToRgba(byte[] ddsBytes, out DdsMetadata meta)
        {
            meta = ParseDdsHeader(ddsBytes);
            if (meta.IsDxt5)
                return DecodeDxt5ToRgba(ddsBytes, meta.Width, meta.Height, meta.DataOffset);

            if (meta.FourCC == "DXT1" || meta.FourCC == "BC1U" || meta.DxgiFormat == 71 || meta.DxgiFormat == 72)
                return DecodeDxt1ToRgba(ddsBytes, meta.Width, meta.Height, meta.DataOffset);

            if (meta.FormatName == "RGBA8" || meta.FormatName == "R8G8B8A8_UNORM")
            {
                int expected = meta.Width * meta.Height * 4;
                if (ddsBytes.Length >= meta.DataOffset + expected)
                {
                    byte[] raw = new byte[expected];
                    Buffer.BlockCopy(ddsBytes, meta.DataOffset, raw, 0, expected);
                    return raw;
                }
            }

            // Fallback to texconv -> uncompressed R8G8B8A8_UNORM DDS
            string toolType = _config.GetResolvedDdsToolType();
            if (toolType == "texconv")
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "ytd_dec_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                try
                {
                    string inFile = Path.Combine(tempDir, "in.dds");
                    File.WriteAllBytes(inFile, ddsBytes);
                    RunProcess(_config.DdsToolPath, $"-f R8G8B8A8_UNORM -m 1 -nologo -y -o \"{tempDir}\" \"{inFile}\"");
                    byte[] decBytes = File.ReadAllBytes(inFile);
                    var decMeta = ParseDdsHeader(decBytes);
                    int exp = decMeta.Width * decMeta.Height * 4;
                    byte[] rgba = new byte[exp];
                    Buffer.BlockCopy(decBytes, decMeta.DataOffset, rgba, 0, exp);
                    return rgba;
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }

            throw new InvalidOperationException($"DDS formatı ({meta.FormatName}) çözümlenemedi. Lütfen Ayarlar'dan texconv.exe seçin.");
        }

        public static byte[] PatchDxt5BlocksWithMask(
            byte[] originalDds,
            byte[] newlyEncodedDds,
            byte[] binaryMask,
            int maskW,
            int maskH,
            out int touchedBlocks,
            out int totalBlocks)
        {
            touchedBlocks = 0;
            totalBlocks = 0;

            var origMeta = ParseDdsHeader(originalDds);
            var newMeta = ParseDdsHeader(newlyEncodedDds);

            if (!origMeta.IsDxt5 || !newMeta.IsDxt5 || origMeta.Width != newMeta.Width || origMeta.Height != newMeta.Height)
                return NormalizeDx10Bc3ToDxt5(newlyEncodedDds);

            int width = origMeta.Width;
            int height = origMeta.Height;
            int outMips = newMeta.MipCount;

            byte[] stdHeader = BuildStandardDxt5Header(width, height, outMips);
            using (var ms = new MemoryStream())
            {
                ms.Write(stdHeader, 0, stdHeader.Length);

                int origPos = origMeta.DataOffset;
                int newPos = newMeta.DataOffset;
                int mw = width;
                int mh = height;

                for (int mip = 0; mip < outMips; mip++)
                {
                    int bx = Math.Max(1, (mw + 3) / 4);
                    int by = Math.Max(1, (mh + 3) / 4);
                    int mipByteLen = bx * by * 16;

                    bool hasOrigMip = (mip < origMeta.MipCount) && (origPos + mipByteLen <= originalDds.Length);
                    bool hasNewMip = (newPos + mipByteLen <= newlyEncodedDds.Length);

                    if (!hasOrigMip || !hasNewMip)
                    {
                        if (hasNewMip)
                            ms.Write(newlyEncodedDds, newPos, mipByteLen);
                        newPos += mipByteLen;
                        origPos += mipByteLen;
                        mw = Math.Max(1, mw / 2);
                        mh = Math.Max(1, mh / 2);
                        continue;
                    }

                    byte[] mipBuf = new byte[mipByteLen];
                    Buffer.BlockCopy(originalDds, origPos, mipBuf, 0, mipByteLen);

                    int scaleX = Math.Max(1, maskW / mw);
                    int scaleY = Math.Max(1, maskH / mh);

                    for (int yB = 0; yB < by; yB++)
                    {
                        for (int xB = 0; xB < bx; xB++)
                        {
                            bool touched = false;
                            int startY = (yB * 4) * scaleY;
                            int endY = Math.Min(maskH, ((yB + 1) * 4) * scaleY + (mip > 0 ? scaleY : 0));
                            int startX = (xB * 4) * scaleX;
                            int endX = Math.Min(maskW, ((xB + 1) * 4) * scaleX + (mip > 0 ? scaleX : 0));

                            for (int my = Math.Max(0, startY - (mip > 0 ? scaleY : 0)); my < endY && !touched; my++)
                            {
                                int rowOff = my * maskW;
                                for (int mx = Math.Max(0, startX - (mip > 0 ? scaleX : 0)); mx < endX; mx++)
                                {
                                    if (binaryMask[rowOff + mx] > 0)
                                    {
                                        touched = true;
                                        break;
                                    }
                                }
                            }

                            int blockByteOff = (yB * bx + xB) * 16;
                            if (mip == 0)
                            {
                                totalBlocks++;
                                if (touched) touchedBlocks++;
                            }

                            if (touched)
                            {
                                Buffer.BlockCopy(newlyEncodedDds, newPos + blockByteOff, mipBuf, blockByteOff, 16);
                            }
                        }
                    }

                    ms.Write(mipBuf, 0, mipByteLen);
                    origPos += mipByteLen;
                    newPos += mipByteLen;
                    mw = Math.Max(1, mw / 2);
                    mh = Math.Max(1, mh / 2);
                }

                return ms.ToArray();
            }
        }

        public byte[] EncodeRgbaToDxt5Dds(
            byte[] rgba,
            int width,
            int height,
            bool preserveSrgbFlag,
            byte[] originalDds,
            byte[] modifiedFootprintMask,
            out DdsEncodeStats stats)
        {
            if (width % 4 != 0 || height % 4 != 0)
                throw new InvalidOperationException($"DXT5 (BC3) kaydı için genişlik ({width}) ve yükseklik ({height}) 4'ün katı olmalıdır.");

            string toolType = _config.GetResolvedDdsToolType();
            if (toolType == "none" || !File.Exists(_config.DdsToolPath))
            {
                throw new InvalidOperationException(
                    "DDS DXT5 encode işlemi için 'texconv.exe' veya 'CompressonatorCLI.exe' bulunamadı!\n" +
                    "Lütfen Üst Menü -> Ayarlar bölümünden texconv.exe veya CompressonatorCLI.exe yolunu seçin.");
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "ytd_enc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            byte[] encodedDds;

            try
            {
                string rawDdsPath = Path.Combine(tempDir, "source_rgba.dds");
                byte[] rawDdsBytes = BuildUncompressedRgba8Dds(rgba, width, height);
                File.WriteAllBytes(rawDdsPath, rawDdsBytes);

                if (toolType == "texconv")
                {
                    // BC3_UNORM with full mipmaps (-m 0), uniform RGB error (-bc u, no sRGB shift),
                    // and separate alpha mip filtering (-sepalpha, straight unpremultiplied alpha).
                    string args = $"-f BC3_UNORM -m 0 -bc u -sepalpha -nologo -y -o \"{tempDir}\" \"{rawDdsPath}\"";
                    RunProcess(_config.DdsToolPath, args);
                    encodedDds = File.ReadAllBytes(rawDdsPath);
                }
                else
                {
                    string outDdsPath = Path.Combine(tempDir, "encoded_dxt5.dds");
                    int maxDim = Math.Max(width, height);
                    int mipLevels = (int)Math.Floor(Math.Log(maxDim, 2)) + 1;
                    string args = $"-fd BC3 -miplevels {mipLevels} \"{rawDdsPath}\" \"{outDdsPath}\"";
                    RunProcess(_config.DdsToolPath, args);
                    encodedDds = File.ReadAllBytes(outDdsPath);
                }
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }

            encodedDds = NormalizeDx10Bc3ToDxt5(encodedDds);

            stats = new DdsEncodeStats
            {
                ToolUsed = toolType,
                BlockPatched = false,
                TouchedBlocks = 0,
                TotalBlocks = (width / 4) * (height / 4)
            };

            if (_config.BlockLevelDxt5Patch && originalDds != null && modifiedFootprintMask != null)
            {
                byte[] patched = PatchDxt5BlocksWithMask(
                    originalDds,
                    encodedDds,
                    modifiedFootprintMask,
                    width,
                    height,
                    out int touched,
                    out int total);

                if (total > 0)
                {
                    encodedDds = patched;
                    stats.BlockPatched = true;
                    stats.TouchedBlocks = touched;
                    stats.TotalBlocks = total;
                }
            }

            return encodedDds;
        }

        private static void RunProcess(string exePath, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var proc = Process.Start(psi))
            {
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Harici DDS aracı hatası ({Path.GetFileName(exePath)}, kod {proc.ExitCode}):\n{stderr}\n{stdout}");
                }
            }
        }
    }
}
