# RePKG（vendor 副本）

本目录是开源项目 [RePKG](https://github.com/notscuffed/repkg) 官方源码的**原样拷贝**，
只保留本程序需要的两个库工程：

| 工程 | 目标框架 | 作用 |
| --- | --- | --- |
| `RePKG.Core` | netstandard2.0 | PKG / TEX 的数据结构与解析接口 |
| `RePKG.Application` | netstandard2.0 | `PackageReader`、`TexReader`、`TexToImageConverter` 等实现 |

`RePkgGui` 通过 `ProjectReference` 直接引用这两个工程，**没有重新实现任何 PKG / TEX 解析逻辑**。

## 相对上游的改动

仅为工程化适配，不涉及解析逻辑：

1. `SixLabors.ImageSharp` 由 `2.1.9` 提升到 `2.1.13`（同主版本、API 兼容，修掉已知 CVE）
2. 移除未被任何代码使用的 `System.Drawing.Common` 引用

## 许可证

RePKG 以 **MIT** 协议发布，© notscuffed。
本仓库根目录的 [LICENSE](../LICENSE) 已一并保留上游版权声明，本项目同样以 MIT 发布。
