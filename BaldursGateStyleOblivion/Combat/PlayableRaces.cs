using System.IO.Compression;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;

namespace BaldursGateStyleOblivion.Combat;

// Mutagen 0.54.4 reads Oblivion RACE DATA two bytes past its native flags.
// Read playability directly; do not change any race records.
internal static class PlayableRaces
{
    public static Dictionary<FormKey,bool> Read(string path,IOblivionModGetter mod)
    {
        var result=new Dictionary<FormKey,bool>();
        using var stream=File.OpenRead(path);using var reader=new BinaryReader(stream);
        while(stream.Position<stream.Length)
        {
            var tag=new string(reader.ReadChars(4));var size=reader.ReadUInt32();var header=reader.ReadBytes(12);
            if(tag!="GRUP"){stream.Position+=size;continue;}
            var end=stream.Position+size-20;
            if(System.Text.Encoding.ASCII.GetString(header,0,4)!="RACE"){stream.Position=end;continue;}
            while(stream.Position<end)
            {
                var recordTag=new string(reader.ReadChars(4));var recordSize=reader.ReadUInt32();var flags=reader.ReadUInt32();var raw=reader.ReadUInt32();reader.ReadUInt32();
                var data=reader.ReadBytes(checked((int)recordSize));
                if(recordTag!="RACE")continue;
                if((flags&0x40000)!=0){using var compressed=new MemoryStream(data,4,data.Length-4);using var zlib=new ZLibStream(compressed,CompressionMode.Decompress);using var expanded=new MemoryStream();zlib.CopyTo(expanded);data=expanded.ToArray();}
                using var fields=new BinaryReader(new MemoryStream(data));
                while(fields.BaseStream.Position<data.Length)
                {
                    var sub=new string(fields.ReadChars(4));var length=(int)fields.ReadUInt16();
                    if(sub=="XXXX"){length=fields.ReadInt32();sub=new string(fields.ReadChars(4));fields.ReadUInt16();}
                    if(sub=="DATA" && length>=36)
                    {
                        fields.BaseStream.Position+=32;var playable=(fields.ReadUInt32()&1)!=0;
                        var index=(int)(raw>>24);var owner=index==mod.MasterReferences.Count?mod.ModKey:mod.MasterReferences[index].Master;
                        result[new FormKey(owner,raw&0xFFFFFF)]=playable;break;
                    }
                    fields.BaseStream.Position+=length;
                }
            }
        }
        return result;
    }
}