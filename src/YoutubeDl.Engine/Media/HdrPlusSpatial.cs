namespace YoutubeDl.Engine.Media;

internal static class HdrPlusSpatial
{
    internal static byte[] Map(byte[] payload,HdrSpatialGeometry geometry)
    {
        if(!HevcMetadataTransport.HdrPlus(payload)||payload.Length>4096||payload[6]!=1)throw new NotSupportedException("HDR10+ geometry requires application version 1.");var bits=new HdrBits(payload);bits.Skip(56);var windows=bits.Read(2);if(windows is <1 or >3)throw new InvalidDataException("HDR10+ declares an invalid window count.");if(geometry.Padding)throw new NotSupportedException("HDR10+ full-frame histogram remapping for padding is unfinished.");
        for(var window=1;window<windows;window++)
        {
            var start=bits.Position;var x0=bits.Read(16);var y0=bits.Read(16);var x1=bits.Read(16);var y1=bits.Read(16);var cx=bits.Read(16);var cy=bits.Read(16);var angle=bits.Read(8);var inner=bits.Read(16);var outer=bits.Read(16);var minor=bits.Read(16);var overlap=bits.Read(1);
            if(x0>x1||y0>y1||angle>180||inner>outer)throw new InvalidDataException("HDR10+ window or ellipse geometry is invalid.");
            var mapped=new HdrBits();foreach(var value in new[]{geometry.X(x0),geometry.Y(y0),geometry.X(x1),geometry.Y(y1),geometry.X(cx),geometry.Y(cy)})mapped.Put(value,16);mapped.Put(angle,8);foreach(var value in new[]{geometry.Radius(inner),geometry.Radius(outer),geometry.Radius(minor)})mapped.Put(value,16);mapped.Put(overlap,1);for(var i=0;i<mapped.Data.Count;i++)bits.Data[start+i]=mapped.Data[i];
        }
        return bits.Bytes();
    }
}
