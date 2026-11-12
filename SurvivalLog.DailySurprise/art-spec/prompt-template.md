# 喜从天降 事件卡图标 · 统一风格 Prompt 模板

> 读者：负责出图的作者（或代出图的工具操作者）。本文件自包含，配合
> `compositions.md`（45 张逐事件构图）与 `acceptance-checklist.md`（验收）使用。
> 45 张图标在同一张事件卡 UI 上轮换显示，**风格一致性比单张精美更重要**。

---

## 1. 交付物总览

- 45 张 WebP 配图，**统一 4:3 横版 1536×1152**（对齐官方原生事件图——原生即 2400×1792 横版
  WebP，本规格与其同族且体积可控；工具出 2048×1536 后统一缩到 1536×1152 亦可，最终交付必须同尺寸同格式）。
- 格式：WebP（质量 90 左右）。命名：`ds_<事件号小写>.webp`（如 `ds_g1.webp`），完整对照表见 `compositions.md`。
- 放入本工程 `SurvivalLog.DailySurprise\Art\` 目录即可（构建自动打包、游戏内自动落盘，见验收清单）。

## 2. 统一风格模板（直接粘贴到出图工具）

> 风格基准 = **官方原生事件图**（日式动画背景美术，已对照官方素材校准）。
> 45 张完整逐事件成品 prompt 见同目录 `prompts-all45.md`，本模板负责讲清规则与槽位。

### 2.1 中文版（适用于支持中文 prompt 的工具）

```
日式动画电影背景美术风格插画，介于新海诚与吉卜力之间，数字厚涂与平涂混合，
笔触细腻，弱轮廓线无硬描边，冷暖对撞布光：暖色主光源（黄昏橙金或晨光或室内暖灯）
对比青灰紫冷阴影，中低饱和度，细节丰富，空气透视与体积光，电影级构图，
横幅画面，安静克制的生活叙事氛围。
画面主体：{从 compositions.md「画面主体」列粘贴}。
场景要素：{从 compositions.md「场景要素」列粘贴}。
情绪基调：{从 compositions.md「情绪基调」列粘贴}。
```

### 2.2 英文版（适用于 Stable Diffusion 系工具）

```
Japanese animated film background art, style between Makoto Shinkai and
Studio Ghibli, blend of digital impasto and flat painting, delicate
brushwork, soft contour lines without hard outlines, warm-cool contrast
lighting: warm key light (dusk gold / morning glow / indoor lamp) against
blue-gray-purple cool shadows, medium-low saturation, rich detail,
atmospheric perspective and volumetric light, cinematic composition,
4:3 horizontal landscape framing, quiet restrained slice-of-life mood.
subject: {画面主体英文}.
scene: {场景要素英文}.
mood: {情绪基调英文}.
```

### 2.3 负面提示词（每张都要带，防穿帮）

```
text, letters, numbers, watermark, signature, logo, frame, border,
photorealistic, photo, 3d render, cgi, cyberpunk, neon, oversaturated,
gore, blood, chibi, q-version, sketch, blurry, lowres, deformed,
extra limbs, bad anatomy
```

要点：

- **禁一切文字**——图在事件卡上会缩放显示，任何字母/数字/水印都会糊成噪点。
- **禁写实照片风、禁 3D 渲染风**——45 张必须都是日式动画背景美术插画，混入一张照片风立刻穿帮。
- **禁 Q 版/草图风**（chibi / q-version / sketch）——人物一律日系写实动画比例。
- **禁高饱和霓虹/赛博**——风格对齐官方原生事件图，色调统一走中低饱和+冷暖对撞布光。

## 3. 变量槽位说明

模板只留三个槽，全部从 `compositions.md` 对应行照抄，不要自己改写：

| 槽位 | 来源列 | 说明 |
|---|---|---|
| 画面主体 | 「画面主体」 | 这张图**必须画出来的东西**（一句话一个视觉焦点） |
| 场景要素 | 「场景要素」 | 主体所在的环境/光线/视角提示 |
| 情绪基调 | 「情绪基调」 | 整张图的情绪关键词，喂给"mood"槽 |

示例（G1 救灾空投，成品 prompt 长这样）：

```
日式动画电影背景美术风格插画，介于新海诚与吉卜力之间，……（锁定段原样）
画面主体：军绿色救灾箱歪在地上，皱瘪的降落伞搭在箱上，几包压缩饼干从裂口滚出。
场景要素：清晨空地，低角度晨光，尘土微扬。
情绪基调：意外之喜、结实可靠。
```

## 4. 建议参数与一致性技巧

| 项 | 建议 |
|---|---|
| 分辨率 | 1536×1152（4:3 横版；工具支持不佳时 2048×1536 出图后缩到 1536×1152） |
| 格式 | WebP，质量 90 左右（禁 JPG 压缩噪点、禁 PNG——45 张必须同格式） |
| 每张候选数 | 一次出 4~8 张，挑最贴 compositions.md 描述的 1 张 |
| 一致性技巧①（垫官方参考图，首选） | 图生图 / style reference / IP-Adapter 垫 1~2 张**官方原生事件图**，**强度 0.4~0.6**，再喂三槽 prompt——垫图比固定 seed 更能保证与官方风格一致。官方图库（608 张，自选喜欢的）：`E:\SteamLibrary\steamapps\common\Survival Log\SurvivalLog_Data\StreamingAssets\WebUI\Res\EventNew\` |
| 一致性技巧②（固定 seed 系） | 先用 G1 调出满意的一张并**记下 seed**；后续 44 张沿用同一 seed（或 ±1 微调），只换三个槽位的内容——同模板+同 seed 是风格统一的最大概率做法 |
| 一致性技巧③（成批复核） | 每定稿 10 张，把它们并排放在一张大图里过目一眼（见验收清单"风格一致性人工过目项"） |

## 5. 出图后自检（每张 30 秒）

1. 图里**没有任何文字/字母/数字/水印**。
2. 主体清晰，缩到缩略图仍能认出是什么（事件卡实际显示不大——可以缩图后眯眼看一眼）。
3. 冷暖对撞光在（暖主光/青灰紫冷影），色调与已定稿的一致，不突然出现荧光色整图。
4. 内容与 `compositions.md` 该行的画面主体对得上（画的是"救灾箱+降落伞"，不是泛泛的"好人好事"）。
5. 命名为该行「命名」列的文件名（`ds_g1.webp`，全小写），导出 WebP（q90），放入 `Art\`。

## 6. 边界情况

- **人物出现时**：日系写实动画比例，背影/侧影/远景优先，不画正面面部特写，避免 45 张里人物长相打架。
- **霉运事件（B/M/H/D 段）**：情绪靠色调压暗一点和场景细节表达，**不画血腥画面**（游戏整体基调是生活流）。
- **灾厄三张（D1~D3）**：允许比其余图更冷、更暗的局部用色，但笔触与整体画风保持一致。
- 工具偶尔把"末世"画成战斗/丧尸场面：在负面提示词里追加 `zombie, battle, war, weapon, explosion`。
