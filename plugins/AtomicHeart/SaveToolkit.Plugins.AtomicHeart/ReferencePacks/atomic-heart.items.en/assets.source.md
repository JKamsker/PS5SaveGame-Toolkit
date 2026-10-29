# Atomic Heart asset provenance

Retrieved 2026-10-29. Processing happened in `%TEMP%/atomicheart-assets` using Python `-I` and Pillow 12.2.0; downloaded data was not executed. PNG/JPEG headers were verified with Pillow before decoding; embedded images are WebP or PNG, at most 64x64. Identical output hashes were checked; no duplicates. Full screenshots and downloaded originals are not embedded.

## Rights and sources

- Official renders and short English excerpts: [Mundfish weapons](https://ah.mundfish.com/weapons/), Slimao Ltd. / Mundfish. The site credits Slimao Ltd. as rights holder. Licence: `Rights reserved by Slimao Ltd.; used with attribution per owner decision 2026-10-29`. Each item links its individual weapon page. Excerpts are short, not complete official descriptions. English weapon names are official site names; KS-23 comes from the game UI. Native joins remain Inferred on PS5.
- Game UI crops, resource/module names, effects and recipe costs: [Atomic Heart Complete Crafting Guide](https://whatifgaming.com/atomic-heart-crafting-guide/), Bilawal Bashir / WhatIfGaming, published 2023-02-22; game artwork by Slimao Ltd. / Mundfish. Licence: `Rights reserved by Slimao Ltd. and WhatIfGaming; used with attribution per owner decision 2026-10-29`. Only game UI glyphs/renders are cropped; site logos and full screenshots are excluded.
- Resource icon order is shown in NORA; the named Biomaterials panel confirms the third glyph. Names/order cross-checked against the crafting guide resource illustrations. Only Metal Parts, Synthetic Material and Small Neuromed Capsule are joined to exact sample IDs. No inferred Neuropolymer/Jelly join, other resource ID or module ID is invented.

## Embedded image audit

| File | Exact source image | Changes | Bytes | Embedded SHA-256 |
| --- | --- | --- | ---: | --- |
| icons/railgun.webp | https://ah.mundfish.com/wp-content/uploads/07-10.jpg | Cropped transparent bounds where present; resized to fit 64x64; centered; compact encoded. | 582 | `ee453b3cf0988834f4103dca343bc7898af36c0bb691a34c2cd59c556e2f3500` |
| icons/fat_boy.webp | https://ah.mundfish.com/wp-content/uploads/krepysh_4k_7.png | Cropped transparent bounds where present; resized to fit 64x64; centered; compact encoded. | 680 | `3753f4d7e7ca4fb5af1e04a15a185a1c9362969c558f674fe65f7ba5184a8d63` |
| icons/dominator.webp | https://ah.mundfish.com/wp-content/uploads/dominator_4k_10.png | Cropped transparent bounds where present; resized to fit 64x64; centered; compact encoded. | 604 | `9dfe97eb2ee0b8aa5a030dfd2995f68c4f40db20687757672c68af75e8314f76` |
| icons/kalash.webp | https://ah.mundfish.com/wp-content/uploads/ak47_4k_v1.png | Cropped transparent bounds where present; resized to fit 64x64; centered; compact encoded. | 500 | `92fe35e9e4ddaa0040f12a4101aa424625ca13d487be71acc1d44139f229b8c4` |
| icons/pm.webp | https://ah.mundfish.com/wp-content/uploads/pm_4k_9.png | Cropped transparent bounds where present; resized to fit 64x64; centered; compact encoded. | 358 | `a7e1fab2aa9ccc050033dc5db7a928abc32c4d6c96de56b30d531941118334f6` |
| icons/shotgun.webp | https://ah.mundfish.com/wp-content/uploads/ks23_final_preview010_main-camera.png | Cropped transparent bounds where present; resized to fit 64x64; centered; compact encoded. | 432 | `27fa69797024b27a2be8c3aa72c37e6f24715827716fd4a4f189ad71fc4da53b` |
| icons/fox.webp | https://ah.mundfish.com/wp-content/uploads/lisa_final_preview_main-camera.png | Cropped transparent bounds where present; resized to fit 64x64; centered; compact encoded. | 682 | `f7d893618b6ba6abdc4bf9f2e3f441d1de94d610f7d20bdda6c5560fefe59065` |
| icons/pashtet.webp | https://ah.mundfish.com/wp-content/uploads/pashtet_final_preview003_main-camera-1.png | Cropped transparent bounds where present; resized to fit 64x64; centered; compact encoded. | 580 | `242052d3ca9a2a8c1413e07c05e18077463a6c770e84f14503ce5761ea51cf70` |
| icons/snowball.webp | https://ah.mundfish.com/wp-content/uploads/snezhock_final_prevew014_main-camera.png | Cropped transparent bounds where present; resized to fit 64x64; centered; compact encoded. | 562 | `55f66c7cc0bda151844a6564627001a86c753e413543a1441bd1714998f949db` |
| icons/zvezdochka.webp | https://ah.mundfish.com/wp-content/uploads/zvezdochka4k-3.png | Cropped transparent bounds where present; resized to fit 64x64; centered; compact encoded. | 1004 | `89275b7b640eaddad40d88aac935892557825f254a912b4c85b1b13e1d4cc9c4` |
| icons/electro.webp | https://ah.mundfish.com/wp-content/uploads/electro_4k_5.png | Cropped transparent bounds where present; resized to fit 64x64; centered; compact encoded. | 558 | `ea48a957131f8ea21197b93466f69b976428dc92b014385c1f174e9b613dd750` |
| icons/swede.webp | https://ah.mundfish.com/wp-content/uploads/shved_final_preview005_main-camera-1.png | Cropped transparent bounds where present; resized to fit 64x64; centered; compact encoded. | 556 | `67d55ffc383732df109cdaf305b39573d25973f784e4cf01afe63ef949735888` |
| icons/metal-parts.webp | https://whatifgaming.com/wp-content/uploads/2023/02/Atomic-Heart-Crafting-Weapons.jpg | Cropped NORA header icon [1415,44,1470,98]; centered in 64x64; no upscaling. | 322 | `2502198c0338c1e2fade86dabc4d58a9cc90d9f8484c7696d9b7b229710485a0` |
| icons/synthetic-material.webp | https://whatifgaming.com/wp-content/uploads/2023/02/Atomic-Heart-Crafting-Weapons.jpg | Cropped NORA header icon [1473,44,1528,98]; centered in 64x64; no upscaling. | 346 | `544943c1cc8eea232061fb1306d24bf0df38fb9ef5fbdae3500235ebd8d1400a` |
| icons/biomaterials.webp | https://whatifgaming.com/wp-content/uploads/2023/02/Atomic-Heart-Crafting-Weapons.jpg | Cropped NORA header icon [1531,44,1586,98]; centered in 64x64; no upscaling. | 376 | `36cae2f3a09a47bed04f32de6a05664b0e091b5e66709003ec33a196576064d4` |
| icons/superconductor.webp | https://whatifgaming.com/wp-content/uploads/2023/02/Atomic-Heart-Crafting-Weapons.jpg | Cropped NORA header icon [1589,44,1644,98]; centered in 64x64; no upscaling. | 538 | `53be9854b57aafae892d9b0787d5b0d48d8f8883ff984a958e56aa349c133ea9` |
| icons/chemistry.webp | https://whatifgaming.com/wp-content/uploads/2023/02/Atomic-Heart-Crafting-Weapons.jpg | Cropped NORA header icon [1647,44,1702,98]; centered in 64x64; no upscaling. | 310 | `69ee811a72848b09344694c4be2caa5096f14c314eed7d3cadc52f1a869ffd6f` |
| icons/microelectronics.webp | https://whatifgaming.com/wp-content/uploads/2023/02/Atomic-Heart-Crafting-Weapons.jpg | Cropped NORA header icon [1705,44,1760,98]; centered in 64x64; no upscaling. | 396 | `d1bdf04875c87a86d0da2c5bff90a9094b56ce459616d3e3f434c87159df4a1f` |
| icons/energy-module.webp | https://whatifgaming.com/wp-content/uploads/2023/02/Atomic-Heart-Crafting-Weapons.jpg | Cropped NORA header icon [1763,44,1818,98]; centered in 64x64; no upscaling. | 420 | `1ddbda139d88ef563835374df0670bac05e4e62b16c8f68374946a5869b84dca` |
| icons/neuromodule.webp | https://whatifgaming.com/wp-content/uploads/2023/02/Atomic-Heart-Crafting-Weapons.jpg | Cropped NORA header icon [1821,44,1876,98]; centered in 64x64; no upscaling. | 552 | `b3266d17e02a01dc1cdd730269ad9a0fa9f7166d16b029beda4fc3c14b2a57f1` |
| icons/small-neuromed.webp | https://whatifgaming.com/wp-content/uploads/2023/02/Atomic-Heart-Crafting-consumables.jpg | Cropped item render [847,204,1032,416]; fitted to 64x64. | 1198 | `4ed561aa37c9036a69f629633ea316be38ffca5ddf4c7cb9f2b75245a6f6c2cf` |
| icons/ks23-additional-gas-vent.webp | https://whatifgaming.com/wp-content/uploads/2023/02/Installing-Gun-Grip-on-Shotgun-.jpg | Cropped unobscured top handle glyph [98,50,143,95]; centered in 64x64; no upscaling. | 494 | `aab69a955d8dcab1486247a0823127916a9b656fdb42f196b14e59363ee14179` |
| icons/ks23-cartridge-module.webp | https://whatifgaming.com/wp-content/uploads/2023/02/Installing-Upgrade-on-a-Weapon.jpg | Cropped module glyph [397, 198, 442, 243]; centered in 64x64; no upscaling. | 574 | `e564fb2d32b6c4933f888c28bf66f75ea725f330609a0cceb762b474948942c4` |

Total new binary assets: **12624 bytes**, 23 unique icons (12 weapon renders, 8 resources, 2 modules, 1 consumable). No map background: the saved-level index has no supported geographic transform or positioned markers.

## Downloaded original fingerprints

| Source image | Original SHA-256 |
| --- | --- |
| https://ah.mundfish.com/wp-content/uploads/07-10.jpg | `f54dc8b169434277b35a64524a607cf192a1ea850be6dc769c91b09cd72f522b` |
| https://ah.mundfish.com/wp-content/uploads/krepysh_4k_7.png | `dcc21fe0c73f1e86e952c5421ed51dff1f50fbd28a747352d22b0682acdccb08` |
| https://ah.mundfish.com/wp-content/uploads/dominator_4k_10.png | `e4e37a1729512851f31dbe2f6e9a1bd3711175ad76395f4cc29f06236d0e258d` |
| https://ah.mundfish.com/wp-content/uploads/ak47_4k_v1.png | `c8cfd3928bcdc4ca847ca17b759c9331db04932ffedc35d870ef094c2587c7b7` |
| https://ah.mundfish.com/wp-content/uploads/pm_4k_9.png | `ce3e52408f2ca9f0048ed5b4819884a8cb77f4f366c0a1f203635a4ec97de0a4` |
| https://ah.mundfish.com/wp-content/uploads/ks23_final_preview010_main-camera.png | `555758880f451ceb839c574debf7fba65d53e056236579588ea8d9c8026067b3` |
| https://ah.mundfish.com/wp-content/uploads/lisa_final_preview_main-camera.png | `d302c908e0f5fa0a83e796bc24a058c689ce7cc8a126cc51d4c240d39993892e` |
| https://ah.mundfish.com/wp-content/uploads/pashtet_final_preview003_main-camera-1.png | `9cc03ba0a476d3c85c52b76e0c3f9ed9254fcfb867e035bef9d92c836bec6c04` |
| https://ah.mundfish.com/wp-content/uploads/snezhock_final_prevew014_main-camera.png | `ca4b312855468de182916e92bc135c3fae30600277cf07bfc3cdee61d5fbd613` |
| https://ah.mundfish.com/wp-content/uploads/zvezdochka4k-3.png | `adb6f6b5070adfeb3f85dbf429155b99c34a546a9712a9971edf4ab5d8b8d078` |
| https://ah.mundfish.com/wp-content/uploads/electro_4k_5.png | `a265dd5b534518d36d43f89ebccbea18a34559f55c8efdf0e08cca0575b776ae` |
| https://ah.mundfish.com/wp-content/uploads/shved_final_preview005_main-camera-1.png | `26612db9300d1bde2702fb915574ab8cf15cce8e97bff494ab8d15ee373426b7` |
| https://whatifgaming.com/wp-content/uploads/2023/02/Atomic-Heart-Crafting-Weapons.jpg | `2fee92e681f9ccbc2acaa3597df77328aeeed41df00f3de25f11813cbcf07c96` |
| https://whatifgaming.com/wp-content/uploads/2023/02/Atomic-Heart-Crafting-consumables.jpg | `b66cf8434291d87bc6db437d779f041fd6c7d5c741a5df2d57b21b728516090e` |
| https://whatifgaming.com/wp-content/uploads/2023/02/Installing-Gun-Grip-on-Shotgun-.jpg | `a0f98ddc833cdb259aead25906201249486ac274112fb222da96825534c36c27` |
| https://whatifgaming.com/wp-content/uploads/2023/02/Installing-Upgrade-on-a-Weapon.jpg | `2fb51d90261e15a2eaa1fa8d61432054227413894d6515e49fda01ccfa1fa4f5` |
