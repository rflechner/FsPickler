﻿namespace Nessos.FsPickler

    open System
    open System.IO
    open System.Text
    open System.Threading
    open System.Runtime.Serialization

    open Microsoft.FSharp.Core.LanguagePrimitives

    open Nessos.FsPickler

    [<AutoOpen>]
    module private BclBinaryFormatUtils =

        // each object is serialized with a 32 bit header
        //
        //   1. the first byte is a fixed identifier
        //   2. the second byte contains TypeInfo
        //   3. the third byte contains PicklerInfo
        //   3. the fourth byte contains ObjectFlags
        
        [<Literal>]
        let initByte = 130uy

        // this binary format uses Buffer.BlockCopy for performance
        // and thus does little to handle endianness issues.
        // To avoid silent data corruption, record the serializer's
        // endianness setting at the beginning of the serialization stream.
        let isLittleEndian = BitConverter.IsLittleEndian

        let inline createHeader (typeInfo : TypeKind) (picklerInfo : PicklerInfo) (flags : ObjectFlags) =
            uint32 initByte 
            ||| (uint32 typeInfo <<< 8) 
            ||| (uint32 picklerInfo <<< 16) 
            ||| (uint32 flags <<< 24)

        let inline readHeader (typeInfo : TypeKind) (picklerInfo : PicklerInfo) (header : uint32) =
            if byte header <> initByte then
                raise <| new InvalidDataException("invalid stream data.")

            let streamTypeInfo = header >>> 8 |> byte |> EnumOfValue<byte, TypeKind>
            if streamTypeInfo <> typeInfo then
                let message = sprintf "expected type '%O', was '%O'." typeInfo streamTypeInfo
                raise <| new InvalidDataException(message)

            let streamPicklerInfo = header >>> 16 |> byte |> EnumOfValue<byte, PicklerInfo>
            if streamPicklerInfo <> picklerInfo then
                let message = sprintf "expected pickler '%O, was '%O'." picklerInfo streamPicklerInfo
                raise <| new InvalidDataException(message)

            header >>> 24 |> byte |> EnumOfValue<byte, ObjectFlags>

        [<Literal>]
        let bufferSize = 256    
        let buffer = new ThreadLocal<byte []>(fun () -> Array.zeroCreate<byte> bufferSize)

        /// block copy primitive array to stream
        let blockCopy (source : Array, target : Stream) =

            let buf = buffer.Value
            let mutable bytes = Buffer.ByteLength source
            let mutable i = 0

            while bytes > bufferSize do
                Buffer.BlockCopy(source, i, buf, 0, bufferSize)
                target.Write(buf, 0, bufferSize)
                i <- i + bufferSize
                bytes <- bytes - bufferSize

            if bytes > 0 then
                Buffer.BlockCopy(source, i, buf, 0, bytes)
                target.Write(buf, 0, bytes)

        /// copy stream contents to preallocated array
        let blockRead (source : Stream, target : Array) =
            let buf = buffer.Value
            let inline fillBytes (n : int) =
                let mutable read = 0
                while read < n do
                    read <- read + source.Read(buf, 0, n - read)
        
            let mutable bytes = Buffer.ByteLength target
            let mutable i = 0

            while bytes > bufferSize do
                do fillBytes bufferSize
                Buffer.BlockCopy(buf, 0, target, i, bufferSize)
                i <- i + bufferSize
                bytes <- bytes - bufferSize

            if bytes > 0 then
                do fillBytes bytes
                Buffer.BlockCopy(buf, 0, target, i, bytes)
  

    type BclBinaryPickleWriter internal (stream : Stream, encoding : Encoding, leaveOpen) =

        let bw = new BinaryWriter(stream, encoding, leaveOpen)

        interface IPickleFormatWriter with
            member __.BeginWriteRoot (id : string) =
                bw.Write initByte
                bw.Write isLittleEndian
                bw.Write id

            member __.EndWriteRoot () = ()

            member __.BeginWriteBoundedSequence _ length = bw.Write length
            member __.EndWriteBoundedSequence () = ()

            member __.BeginWriteUnBoundedSequence _ = ()
            member __.WriteHasNextElement hasNext = bw.Write hasNext

            member __.BeginWriteObject typeFlags picklerFlags tag objectFlags =
                let header = createHeader typeFlags picklerFlags objectFlags
                bw.Write header

            member __.EndWriteObject () = ()

            member __.WriteBoolean _ value = bw.Write value
            member __.WriteByte _ value = bw.Write value
            member __.WriteSByte _ value = bw.Write value

            member __.WriteInt16 _ value = bw.Write value
            member __.WriteInt32 _ value = bw.Write value
            member __.WriteInt64 _ value = bw.Write value

            member __.WriteUInt16 _ value = bw.Write value
            member __.WriteUInt32 _ value = bw.Write value
            member __.WriteUInt64 _ value = bw.Write value

            member __.WriteSingle _ value = bw.Write value
            member __.WriteDouble _ value = bw.Write value
            member __.WriteDecimal _ value = bw.Write value

            member __.WriteChar _ value = bw.Write value
            member __.WriteString _ value = 
                if obj.ReferenceEquals(value, null) then bw.Write true
                else
                    bw.Write false
                    bw.Write value

            member __.WriteDate _ value = bw.Write value.Ticks
            member __.WriteTimeSpan _ value = bw.Write value.Ticks
            member __.WriteGuid _ value = bw.Write (value.ToByteArray())

            member __.WriteBigInteger _ value = 
                let data = value.ToByteArray()
                bw.Write data.Length
                bw.Write data

            member __.WriteBytes _ value = 
                if obj.ReferenceEquals(value, null) then bw.Write -1
                else
                    bw.Write value.Length
                    bw.Write value

            member __.IsPrimitiveArraySerializationSupported = true
            member __.WritePrimitiveArray _ array = bw.Flush() ; blockCopy(array, stream) ; stream.Flush()

            member __.Dispose () = bw.Dispose()

    and BclBinaryPickleReader internal (stream : Stream, encoding, leaveOpen) =
        let br = new BinaryReader(stream, encoding, leaveOpen)

        interface IPickleFormatReader with
            
            member __.Dispose () = br.Dispose ()

            member __.BeginReadRoot () =
                if br.ReadByte () <> initByte then
                    raise <| new InvalidDataException("invalid initialization byte.")

                if br.ReadBoolean () <> isLittleEndian then
                    if isLittleEndian then
                        raise <| new InvalidDataException("serialized data is big-endian.")
                    else
                        raise <| new InvalidDataException("serialized data is little-endian.")

                br.ReadString()

            member __.EndReadRoot () = ()

            member __.BeginReadObject typeFlags picklerFlags tag =
                let header = br.ReadUInt32()
                readHeader typeFlags picklerFlags header

            member __.EndReadObject () = () 

            member __.BeginReadBoundedSequence _ = br.ReadInt32 ()
            member __.EndReadBoundedSequence () = ()

            member __.BeginReadUnBoundedSequence _ = ()
            member __.ReadHasNextElement () = br.ReadBoolean ()

            member __.ReadBoolean _ = br.ReadBoolean()
            member __.ReadByte _ = br.ReadByte()
            member __.ReadSByte _ = br.ReadSByte()

            member __.ReadInt16 _ = br.ReadInt16()
            member __.ReadInt32 _ = br.ReadInt32()
            member __.ReadInt64 _ = br.ReadInt64()

            member __.ReadUInt16 _ = br.ReadUInt16()
            member __.ReadUInt32 _ = br.ReadUInt32()
            member __.ReadUInt64 _ = br.ReadUInt64()

            member __.ReadDecimal _ = br.ReadDecimal()
            member __.ReadSingle _ = br.ReadSingle()
            member __.ReadDouble _ = br.ReadDouble()

            member __.ReadChar _ = br.ReadChar()
            member __.ReadString _ = 
                if br.ReadBoolean() then null
                else
                    br.ReadString()

            member __.ReadDate _ = let ticks = br.ReadInt64() in DateTime(ticks)
            member __.ReadTimeSpan _ = let ticks = br.ReadInt64() in TimeSpan(ticks)
            member __.ReadGuid _ = let bytes = br.ReadBytes(16) in Guid(bytes)

            member __.ReadBigInteger _ =
                let length = br.ReadInt32()
                let data = br.ReadBytes(length)
                new System.Numerics.BigInteger(data)

            member __.ReadBytes _ = 
                let length = br.ReadInt32() 
                if length < 0 then null
                else br.ReadBytes(length)

            member __.IsPrimitiveArraySerializationSupported = true
            member __.ReadPrimitiveArray _ array = blockRead(stream, array)


    and BclBinaryPickleFormatProvider () =

        interface IBinaryPickleFormatProvider with
            member __.Name = "BclBinary"

            member __.CreateWriter (stream, encoding, leaveOpen) = new BclBinaryPickleWriter(stream, encoding, leaveOpen) :> _
            member __.CreateReader (stream, encoding, leaveOpen) = new BclBinaryPickleReader(stream, encoding, leaveOpen) :> _