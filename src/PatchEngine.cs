using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class PatchEngine {
    static uint U32(byte[] b,int o){return BitConverter.ToUInt32(b,o);}
    static int I32(byte[] b,int o){return BitConverter.ToInt32(b,o);}
    static ushort U16(byte[] b,int o){return BitConverter.ToUInt16(b,o);}
    static void W32(byte[] b,int o,uint v){Array.Copy(BitConverter.GetBytes(v),0,b,o,4);}
    static uint Align(uint n,uint a){return (n+a-1)/a*a;}
    public static string Hash(byte[] bytes){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();}
    public static string FileHash(string file){using(var s=File.OpenRead(file))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();}
    static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
    static bool HasBadResult(byte[] b,int start,int end){
        start=Math.Max(0,start); end=Math.Min(b.Length-5,end);
        for(int i=start;i<=end;i++){
            if(b[i]>=0xb8 && b[i]<=0xbf && (U32(b,i+1)==0xbad00001 || U32(b,i+1)==0xbad00002)) return true;
        }
        return false;
    }
    static bool BytesAt(byte[] b,int o,byte[] value){
        if(o<0 || o+value.Length>b.Length)return false;
        for(int i=0;i<value.Length;i++)if(b[o+i]!=value[i])return false;
        return true;
    }
    public static byte[] Core(byte[] original){
        Require(original.Length>0x400,"NGX core is too small");
        int pe=checked((int)U32(original,0x3c));
        Require(pe>0 && pe+24<original.Length && original[0]=='M' && original[1]=='Z' && U32(original,pe)==0x00004550,"x64 PE core required");
        int count=U16(original,pe+6), opt=pe+24, sections=opt+U16(original,pe+20);
        Require(U16(original,pe+4)==0x8664 && U16(original,opt)==0x20b && count>0 && count<32,"Unexpected NGX section table");
        int textRaw=0,textSize=0;
        for(int k=0;k<count;k++){
            int s=sections+40*k; Require(s+40<=original.Length,"Truncated NGX section table");
            string name=Encoding.ASCII.GetString(original,s,Math.Min(8,original.Length-s)).TrimEnd('\0');
            if(name==".text"){textRaw=checked((int)U32(original,s+20));textSize=checked((int)U32(original,s+16));}
        }
        Require(textRaw>0 && textSize>0 && textRaw+textSize<=original.Length,"NGX code section is invalid");
        string originalHash=Hash(original);
        if(originalHash=="91d3742f0df3f2dd85141fa39ea257f7b9243c3e6755cd7fea894c721e7491aa"){
            byte[] outputKnown=(byte[])original.Clone();
            foreach(int offset in new int[]{0x3f7b,0x641fb,0x66bfb}){
                Require(BytesAt(original,offset,new byte[]{0x84,0xc0,0x0f,0x85}),"Unexpected legacy NGX branch");
                int target=checked(offset+8+I32(original,offset+4));
                outputKnown[offset+2]=0xe9;
                Array.Copy(BitConverter.GetBytes(checked(target-(offset+7))),0,outputKnown,offset+3,4);
                outputKnown[offset+7]=0x90;
            }
            return outputKnown;
        }
        List<int> candidates=new List<int>();
        for(int i=textRaw;i+8<=textRaw+textSize;i++){
            if(!BytesAt(original,i,new byte[]{0x84,0xc0,0x0f,0x85}))continue;
            if(i<textRaw+5 || original[i-5]!=0xe8)continue;
            int target=checked(i+8+I32(original,i+4));
            Require(target>=textRaw && target<textRaw+textSize,"NGX branch target is outside code");
            bool modern=target>i && target-i<=0x300 && BytesAt(original,i+8,new byte[]{0x4d,0x85,0xf6}) && (HasBadResult(original,target-0x40,target+0x400) || HasBadResult(original,i+8,i+0x400));
            if(modern)candidates.Add(i);
        }
        Require(candidates.Count>=3 && candidates.Count<=12,"Unsupported NGX core layout: hardware checks were not identified safely");
        byte[] output=(byte[])original.Clone();
        foreach(int offset in candidates){
            int target=checked(offset+8+I32(original,offset+4));
            if(target<offset){
                // Older cores fall through into the rejection log. Keep the original target, but make it unconditional.
                output[offset+2]=0xe9;
                Array.Copy(BitConverter.GetBytes(checked(target-(offset+7))),0,output,offset+3,4);
                output[offset+7]=0x90;
            }else{
                // Newer cores branch to the rejection path. NOP the conditional jump and keep the normal fall-through.
                for(int k=2;k<8;k++)output[offset+k]=0x90;
            }
        }
        Require(Hash(output)!=Hash(original),"NGX core has no patchable hardware checks");
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
