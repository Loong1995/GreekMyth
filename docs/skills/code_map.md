# 战法代码对照（三阵营文档 · 全 id 索引）

> 不写公式；语义见 olympus / heroes / sea_underworld。中文名见 `battle/names.py`。

## 一、武将 template_id ↔ trait ↔ 自带

### 奥林匹斯 olympus

| template_id | trait_id | 自带 skill_id | 拆解 |
|---|---|---|---|
| zeus | duoqing | thunder_oracle | zeus_bolt |
| athena | mingrui | athena_aegis | athena_guard |
| ares | haozhan | ares_warfury | ares_frenzy |
| apollo | guangming | delphi_revelation | apollo_blessing |
| asclepius | renxin | asclepius_oracle | asclepius_kiss |
| artemis | guyue | artemis_hunt | artemis_arrow |
| nike | qiusheng | nike_wings | nike_paean |

### 英雄 heroes

| template_id | trait_id | 自带 | 拆解 / 隐藏 |
|---|---|---|---|
| achilles | aoman | achilles_wrath | achilles_thrust |
| patroclus | bonong | patroclus_standin | patroclus_armor |
| heracles | lumang | heracles_trials | heracles_counter |
| perseus | jiebao | perseus_relics | perseus_flash；隐藏 perseus_mirror |
| hector | zhonglie | hector_warcry | hector_assault |
| atalanta | zhuping | atalanta_swift | atalanta_dash |
| paris | qiaoshe | paris_fatal_arrow | paris_heelseek |
| ajax | jianren | ajax_shield | ajax_bulwark |
| jason | haozhao | jason_expedition | jason_command |
| castor | bingpei | castor_twin | castor_chase |

### 海域·冥界（文档合册；faction 分 sea / underworld）

| template_id | faction | trait_id | 自带 | 拆解 |
|---|---|---|---|---|
| poseidon | sea | jichou | poseidon_oracle | poseidon_torrent |
| amphitrite | sea | roubo | amphitrite_tide | amphitrite_grace |
| triton | sea | zhongyong | triton_horn | triton_surge |
| siren | sea | meihuo | siren_song | siren_charm |
| scylla | sea | tanshi | scylla_maw | scylla_bite |
| odysseus | sea | moushen | odysseus_trojan | odysseus_feint |
| calypso | sea | jiliu | calypso_detain | calypso_rime |
| hades | underworld | weiquan | hades_underworld_dominion | hades_soul_drain |
| medusa | underworld | guyuan | medusa_gaze | medusa_glance |
| persephone | underworld | huichun | persephone_seasons | persephone_sprout |
| charon | underworld | — | charon_ferry | charon_ferryman |
| thanatos | underworld | lengku | thanatos_scythe | thanatos_gaze |
| cerberus | underworld | huzhu | cerberus_bite | cerberus_guard |
| hermes | underworld | jiaoxia | hermes_oracle | hermes_jest |
| hecate | underworld | chalou | hecate_torch | hecate_pyre |

## 二、常用机制词 → 代码

| 中文 | 代码 |
|---|---|
| 追击 | `TIMING_PURSUIT` |
| 准备被动/神谕 | `TIMING_PREPARE` / `is_oracle` |
| 连发 | `burst_rate_bps` / `burst_no` / `burst_rate_up_bps` |
| 协击 | `perform_coordinated_attack` / `kind=coordinated` |
| 连击 | `combo_rate_bps` |
| 格挡 | `block` / `block_charges` / `block_rate_bps` |
| 暴伤加成 | `crit_damage_up_bps` |
| 后排 | `is_backline` position 4~6 |
| 清醒 / 恐惧 / 诅咒 | `clear_mind` / `fear` / `curse` |
| 洪水/怒涛 | `flood` |
| 先攻 | `first_strike` |

## 三、timing 常量

`battle/skills.py`：`TIMING_ACTIVE` / `TIMING_PREPARE` / `TIMING_PURSUIT`。

## 四、维护

新增战法：先改分册效果段 + 本表 + `names.py`，再改对应 `skills_*.py`。
