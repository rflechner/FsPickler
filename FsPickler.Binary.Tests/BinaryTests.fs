﻿namespace A

    open System
    open System.IO

    open Nessos.FsPickler.Binary

    open NUnit.Framework

    open FsCheck

    [<TestFixture>]
    module ``Binary Tests`` =

        type TestCase =
            | Bool of bool
            | Byte of byte
            | SByte of sbyte
            | Bytes of byte []
            | Char of char
            | Single of single
            | Double of double
            | Decimal of decimal
            | Int16 of int16
            | Int32 of int
            | Int64 of int64
            | UInt16 of uint16
            | UInt32 of uint32
            | UInt64 of uint64
            | Guid of Guid
            | String of string
        with
            member c.IsNaN =
                match c with
                | Single f -> Single.IsNaN f
                | Double f -> Double.IsNaN f
                | _ -> false

            member c.Write (bw : BinaryWriter) =
                match c with
                | Bool b -> bw.Write b
                | Byte b -> bw.Write b
                | SByte b -> bw.Write b
                | Int16 n -> bw.Write n
                | Int32 n -> bw.Write n
                | Int64 n -> bw.Write n
                | UInt16 n -> bw.Write n
                | UInt32 n -> bw.Write n
                | UInt64 n -> bw.Write n
                | Bytes bs -> bw.Write bs
                | Char c -> bw.Write c
                | Single s -> bw.Write s
                | Double d -> bw.Write d
                | Decimal d -> bw.Write d
                | Guid g -> bw.Write g
                | String s -> bw.Write s

            member c.Read (br : BinaryReader) =
                match c with
                | Bool b -> Bool <| br.ReadBoolean()
                | Byte b -> Byte <| br.ReadByte()
                | SByte b -> SByte <| br.ReadSByte()
                | Int16 n -> Int16 <| br.ReadInt16()
                | Int32 n -> Int32 <| br.ReadInt32()
                | Int64 n -> Int64 <| br.ReadInt64()
                | UInt16 n -> UInt16 <| br.ReadUInt16()
                | UInt32 n -> UInt32 <| br.ReadUInt32()
                | UInt64 n -> UInt64 <| br.ReadUInt64()
                | Bytes bs -> Bytes <| br.ReadBytes()
                | Char c -> Char <| br.ReadChar()
                | Single s -> Single <| br.ReadSingle()
                | Double d -> Double <| br.ReadDouble()
                | Decimal d -> Decimal <| br.ReadDecimal()
                | Guid g -> Guid <| br.ReadGuid()
                | String s -> String <| br.ReadString()


        let testWriteRead (inputs : TestCase []) =
            use m = new MemoryStream()
            do
                use bw = new BinaryWriter(m)
                for case in inputs do case.Write bw

            m.Position <- 0L
            let br = new BinaryReader(m)

            let testCase (case : TestCase) =
                let case' = case.Read br
                case' = case || case.IsNaN

            inputs |> Array.forall testCase

        [<Test; Repeat(10)>]
        let ``Quick check Writes/Reads`` () =
            Check.Quick (fun x -> x + 1 |> ignore ; true)
//            Check.QuickThrowOnFailure testWriteRead

//        [<Test>]
//        let ``Quick check enlarged Writes/Reads`` () =
//            Check.QuickThrowOnFailure (fun inputs -> testWriteRead [| for i in 1 .. 100 do yield! inputs |])