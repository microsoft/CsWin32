This folder contains winmd files we use for testing. Some come from external sources, and some are small authored fixtures kept in source form in this repository.

Metadata | Source
--|--
ServiceFabric.winmd | [youyuanwu/fabric-metadata](https://github.com/youyuanwu/fabric-metadata/raw/a1bcca6ad6f6a772c9e5ff4bdba80ae5e5f24cfc/.windows/winmd/ServiceFabric.winmd)
CustomIInspectable.winmd | Generated using WinMDGenerator toolchain. Project in the subdirectory [CustomIInspectable](../../CustomIInspectable) with build instructions in [readme.md](../../CustomIInspectable/readme.md).
WnfWithoutStatusSuccess.winmd | Authored fixture for issue #1677 regression coverage. Its source is checked in as [WnfWithoutStatusSuccess.il](WnfWithoutStatusSuccess.il). We keep this one as IL because that is much easier to maintain than reproducing the full end-to-end WinmdGenerator workflow for a focused test asset.
