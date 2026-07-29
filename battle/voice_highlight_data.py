"""专属高光台词数据（docs/character 分册抽取）。

改台词请改分册后重跑：python battle/tools/_extract_duel_voice.py
"""
from __future__ import annotations

# template_id -> scene -> pool_key(target_template|generic) -> lines
HIGHLIGHT_LINES: dict[str, dict[str, dict[str, tuple[str, ...]]]] = {
    'achilles': {
        'highlight': {
            'generic': ('怒追——未止！', '链击，听见了吗？', '一暴击，再一暴击。'),
        },
    },
    'ajax': {
        'highlight': {
            'generic': ('坚壁——全体格挡！', '七重，满层！', '盾在，线在。'),
        },
    },
    'amphitrite': {
        'highlight': {
            'generic': ('潮汐抚愈——全体回生！', '最伤者先救，一个都不落！', '看，海后之泽！'),
        },
    },
    'apollo': {
        'highlight': {
            'generic': ('德尔斐启示——全开！', '日光祝祷，叠至顶！', '此光，即神示！'),
        },
    },
    'ares': {
        'highlight': {
            'generic': ('战神之勇——全开！', '血战场，唯我独狂！', '这一刀，叫战争！'),
        },
    },
    'artemis': {
        'highlight': {
            'generic': ('猎月之矢——贯！', '月影狩猎，后排尽灭！', '此矢，即月神意！'),
        },
    },
    'asclepius': {
        'highlight': {
            'generic': ('蛇杖庇护——全开！', '灵蛇之吻，秽尽愈生！', '此愈，即神恩！'),
        },
    },
    'atalanta': {
        'highlight': {
            'generic': ('全场最快！', '疾风双斩！', '前三回合，我的。'),
        },
    },
    'athena': {
        'highlight': {
            'aegis_reflect': ('埃癸斯反震——退！', '此盾，即神意！', '反震既出，谁敢再近？'),
        },
    },
    'castor': {
        'highlight': {
            'generic': ('双子协战——双刺！', '并辔必成！', '侧翼，命中。'),
        },
    },
    'cerberus': {
        'highlight': {
            'generic': ('三首噬咬！', '守门恶犬！', '冥门，封'),
        },
    },
    'charon': {
        'highlight': {
            'generic': ('船费，结清', '冥河涨潮', '摆渡——到账'),
        },
    },
    'hades': {
        'highlight': {
            'generic': ('冥域，君临', '幽影蔽体，谁敢近身？', '冥河倒灌，尽入王座'),
        },
    },
    'hector': {
        'highlight': {
            'generic': ('战吼——全城听令！', '猛攻，叠满！', '决死一击，为特洛伊！'),
        },
    },
    'heracles': {
        'highlight': {
            'generic': ('试炼又过一格！', '狮皮反噬！', '第十二次，还未满。'),
        },
    },
    'hermes': {
        'highlight': {
            'generic': ('神使戏言！', '神谕，改节奏', '先攻，我先'),
        },
    },
    'jason': {
        'highlight': {
            'generic': ('英雄远征——全开！', '连击率，满！', '号召成势。'),
        },
    },
    'medusa': {
        'highlight': {
            'generic': ('石化凝视！', '蛇瞳一瞥，全场静默', '目光所及，皆为碑林'),
        },
    },
    'nike': {
        'highlight': {
            'generic': ('胜利羽翼——必胜！', '凯歌先攻，阵线在我！', '此翼，即胜意！'),
        },
    },
    'odysseus': {
        'highlight': {
            'generic': ('木马奇谋——该爆了！', '声东击西，三人同误！', '谋深者，赢在时间之后！'),
        },
    },
    'paris': {
        'highlight': {
            'generic': ('致命一矢——中！', '觅踵！', '暴击者的暴击。'),
        },
    },
    'patroclus': {
        'highlight': {
            'generic': ('借刀——落下！', '代战未止！', '甲在，刀在。'),
        },
    },
    'persephone': {
        'highlight': {
            'generic': ('冬春轮转！', '一季愈，一季罚', '春芽，起'),
        },
    },
    'perseus': {
        'highlight': {
            'generic': ('镜盾辉映！', '双段，皆中。', '石化无效——看见了吗？'),
        },
    },
    'poseidon': {
        'highlight': {
            'generic': ('一戟贯阵，余波再震！', '三叉戟下，无完整之阵！', '看清楚了——这才是海皇！'),
        },
    },
    'scylla': {
        'highlight': {
            'generic': ('六首撕咬——第二口！', '撕咬追击，血反哺我！', '峡里开饭——双段！'),
        },
    },
    'siren': {
        'highlight': {
            'generic': ('魅音——最强者先迷！', '迷魂之歌，双人同缚！', '海峡的回声，全是我的！'),
        },
    },
    'thanatos': {
        'highlight': {
            'generic': ('死神镰痕', '死亡凝望', '终局，盖章'),
        },
    },
    'triton': {
        'highlight': {
            'generic': ('海嗣号角——全军格挡！', '父王在侧，号声不绝！', '浪涌起，敌步皆锁！'),
        },
    },
    'zeus': {
        'highlight': {
            'generic': ('神谕应验——落雷！', '天光贯阵，此即神意！', '雷霆连鸣，诸神侧目！'),
            'divine_punishment': ('三雷加身，罪已定——神罚，降！',),
        },
    },
}
