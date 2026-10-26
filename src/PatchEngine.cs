using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class PatchEngine {
    static uint U32(byte[] b,int o){return BitConverter.ToUInt32(b,o);}
    static ushort U16(byte[] b,int o){return BitConverter.ToUInt16(b,o);}
    static void W32(byte[] b,int o,uint v){Array.Copy(BitConverter.GetBytes(v),0,b,o,4);}
    static uint Align(uint n,uint a){return (n+a-1)/a*a;}
    public static string Hash(byte[] bytes){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();}
    public static string FileHash(string file){using(var s=File.OpenRead(file))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();}
    static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
    public static byte[] Core(byte[] original){
        Require(Hash(original)=="91d3742f0df3f2dd85141fa39ea257f7b9243c3e6755cd7fea894c721e7491aa","Unsupported NGX core hash");
        byte[] output=(byte[])original.Clone();
        byte[] before={0x84,0xc0,0x0f,0x85,0x0f,0xff,0xff,0xff};
        byte[] after={0x84,0xc0,0xe9,0x10,0xff,0xff,0xff,0x90};
        foreach(int offset in new int[]{0x3f7b,0x641fb,0x66bfb}){
            for(int k=0;k<8;k++)Require(original[offset+k]==before[k],"Unexpected core branch");
            Array.Copy(after,0,output,offset,8);
        }
        Require(Hash(output)=="cf36298fb8bdcf46e0e5d2f558f9132f9aff86a95db5ce5ab849fbacb4083702","Core output hash mismatch");
        return output;
    }
    sealed class Fixup {public int Position;public string Label;public uint Target;}
    sealed class Code {
        public List<byte> Bytes=new List<byte>();
        public Dictionary<string,int> Labels=new Dictionary<string,int>();
        public List<Fixup> Fixups=new List<Fixup>();
        public void Emit(string hex){foreach(string s in hex.Split(' '))Bytes.Add(Convert.ToByte(s,16));}
        public void Rip(string hex,string label,uint target){Emit(hex);Fixups.Add(new Fixup{Position=Bytes.Count,Label=label,Target=target});Bytes.AddRange(new byte[4]);}
    }
    public static byte[] Exe(byte[] original,string expected,uint callRva,uint loadIat,uint procIat){
        Require(Hash(original)==expected,"Unsupported Magpie executable hash");
        byte[] data=(byte[])original.Clone();int pe=(int)U32(data,0x3c),opt=pe+24,sections=opt+U16(data,pe+20),count=U16(data,pe+6);
        Require(U16(data,pe+4)==0x8664 && U16(data,opt)==0x20b,"x64 PE required");
        Require(count==6 && sections+40*(count+1)<=U32(data,opt+60),"Unexpected section table");
        Func<uint,int> offset=delegate(uint rva){
            for(int k=0;k<count;k++){
                int s=sections+40*k;uint v=U32(data,s+12),n=Math.Max(U32(data,s+8),U32(data,s+16));
                if(rva>=v && rva<v+n)return checked((int)(U32(data,s+20)+rva-v));
            }
            throw new InvalidDataException("RVA outside file sections");
        };
        uint max=0;for(int k=0;k<count;k++){int s=sections+40*k;max=Math.Max(max,U32(data,s+12)+U32(data,s+8));}
        uint va=Align(max,U32(data,opt+32)),raw=Align((uint)data.Length,U32(data,opt+36));
        Code c=new Code();
        c.Emit("48 83 ec 48 48 89 4c 24 20 89 54 24 28 44 89 44 24 30");
        c.Rip("48 8d 0d","dll",0);c.Emit("33 d2 41 b8 00 0a 00 00");c.Rip("ff 15",null,loadIat);
        c.Emit("48 85 c0");c.Rip("0f 84","done",0);c.Emit("48 8b c8");
        c.Rip("48 8d 15","export",0);c.Rip("ff 15",null,procIat);c.Emit("48 85 c0");c.Rip("0f 84","done",0);
        c.Emit("48 8b 4c 24 20 8b 54 24 28 44 8b 44 24 30 ff d0");
        c.Labels["done"]=c.Bytes.Count;c.Emit("48 83 c4 48 c3");uint end=(uint)c.Bytes.Count;
        c.Labels["dll"]=c.Bytes.Count;c.Bytes.AddRange(Encoding.Unicode.GetBytes("30HXCoreLoader.dll\0"));
        c.Labels["export"]=c.Bytes.Count;c.Bytes.AddRange(Encoding.ASCII.GetBytes("LoadPinnedCore\0"));
        while(c.Bytes.Count%4!=0)c.Bytes.Add(0);
        uint unwind=va+(uint)c.Bytes.Count;c.Emit("01 04 01 00 04 82 00 00");
        uint oldPdata=U32(data,opt+112+3*8),oldSize=U32(data,opt+116+3*8),pdata=va+(uint)c.Bytes.Count;
        int oldOffset=offset(oldPdata);for(uint k=0;k<oldSize;k++)c.Bytes.Add(data[oldOffset+k]);
        c.Bytes.AddRange(BitConverter.GetBytes(va));c.Bytes.AddRange(BitConverter.GetBytes(va+end));c.Bytes.AddRange(BitConverter.GetBytes(unwind));
        foreach(Fixup f in c.Fixups){
            uint target=f.Label==null?f.Target:va+(uint)c.Labels[f.Label];
            byte[] relative=BitConverter.GetBytes(checked((int)((long)target-((long)va+f.Position+4))));
            for(int k=0;k<4;k++)c.Bytes[f.Position+k]=relative[k];
        }
        int call=offset(callRva);byte[] expectedCall={0xe8,0xa4,0x0b,0,0};
        for(int k=0;k<5;k++)Require(data[call+k]==expectedCall[k],"Unexpected loader call bytes");
        data[call]=0xe8;Array.Copy(BitConverter.GetBytes(checked((int)((long)va-(callRva+5)))),0,data,call+1,4);
        uint rawSize=Align((uint)c.Bytes.Count,U32(data,opt+36));byte[] output=new byte[raw+rawSize];Array.Copy(data,output,data.Length);c.Bytes.CopyTo(output,(int)raw);
        int header=sections+40*count;Array.Copy(Encoding.ASCII.GetBytes(".hxload"),0,output,header,7);
        W32(output,header+8,(uint)c.Bytes.Count);W32(output,header+12,va);W32(output,header+16,rawSize);W32(output,header+20,raw);W32(output,header+36,0x60000020);
        Array.Copy(BitConverter.GetBytes((ushort)(count+1)),0,output,pe+6,2);
        W32(output,opt+56,Align(va+(uint)c.Bytes.Count,U32(data,opt+32)));W32(output,opt+4,U32(data,opt+4)+rawSize);W32(output,opt+64,0);
        W32(output,opt+112+3*8,pdata);W32(output,opt+116+3*8,oldSize+12);
        return output;
    }
}
