// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomizePlus.Core.Data;

public static class BoneData //todo: DI, do not show IVCS unless IVCS is installed/user enabled it, do not show weapon bones
{
    public enum BoneFamily
    {
        Root,
        Spine,
        Hair,
        Face,
        Eyes,
        Ears,
        Cheeks,
        Lips,
        Tongue,
        Jaw,
        Chest,
        Arms,
        Hands,
        Tail,
        Groin,
        Legs,
        Feet,
        Earrings,
        Hat,
        Cape,
        Armor,
        Skirt,
        Equipment,
        Unknown
    }

    //TODO move the csv data to an external (compressed?) file
    private static readonly string[] BoneRawTable =
    {
        //Codename, Display Name, Bone Family, Parent (if any), Mirrored Bone (if any)
        "n_root,Root,Root,TRUE,FALSE,,", "n_hara,Abdomen,Root,TRUE,FALSE,,", "j_kao,Head,Spine,TRUE,FALSE,j_kubi,",
        "j_kubi,Neck,Spine,TRUE,FALSE,j_sebo_c,", "j_sebo_c,Spine C,Spine,TRUE,FALSE,j_sebo_b,",
        "j_sebo_b,Spine B,Spine,TRUE,FALSE,j_sebo_a,", "j_sebo_a,Spine A,Spine,TRUE,FALSE,j_kosi,",
        "j_kosi,Waist,Spine,TRUE,FALSE,,", "j_kami_a,Hair A,Hair,TRUE,FALSE,j_kao,",
        "j_kami_b,Hair B,Hair,TRUE,FALSE,j_kami_a,", "j_kami_f_l,Hair Front Left,Hair,TRUE,FALSE,j_kao,j_kami_f_r",
        "j_kami_f_r,Hair Front Right,Hair,TRUE,FALSE,j_kao,j_kami_f_l",
        "j_f_mayu_l,Brow Outer Left,Face,TRUE,FALSE,j_kao,j_f_mayu_r",
        "j_f_mayu_r,Brow Outer Right,Face,TRUE,FALSE,j_kao,j_f_mayu_l",
        "j_f_miken_l,Brow Inner Left,Face,TRUE,FALSE,j_kao,j_f_miken_r",
        "j_f_miken_r,Brow Inner Right,Face,TRUE,FALSE,j_kao,j_f_miken_l",
        "j_f_memoto,Bridge,Face,TRUE,FALSE,j_kao,", "j_f_umab_l,Eyelid Upper Left,Face,TRUE,FALSE,j_kao,j_f_umab_r",
        "j_f_umab_r,Eyelid Upper Right,Face,TRUE,FALSE,j_kao,j_f_umab_l",
        "j_f_dmab_l,Eyelid Lower Left,Face,TRUE,FALSE,j_kao,j_f_dmab_r",
        "j_f_dmab_r,Eyelid Lower Right,Face,TRUE,FALSE,j_kao,j_f_dmab_l",
        "j_f_eye_l,Eye Left,Face,TRUE,FALSE,j_kao,j_f_eye_r", "j_f_eye_r,Eye Right,Face,TRUE,FALSE,j_kao,j_f_eye_l",
        "j_f_hige_l,Hrothgar Whiskers Left,Face,FALSE,FALSE,j_kao,j_f_hige_r",
        "j_f_hige_r,Hrothgar Whiskers Right,Face,FALSE,FALSE,j_kao,j_f_hige_l",
        "j_f_lip_l,Lips Left,Face,TRUE,FALSE,j_kao,j_f_lip_r",
        "j_f_lip_r,Lips Right,Face,TRUE,FALSE,j_kao,j_f_lip_l", "j_f_ulip_a,Lip Upper A,Face,TRUE,FALSE,j_kao,",
        "j_f_ulip_b,Lip Upper B,Face,TRUE,FALSE,j_kao,", "j_f_dlip_a,Lip Lower A,Face,TRUE,FALSE,j_kao,",
        "j_f_dlip_b,Lip Lower B,Face,TRUE,FALSE,j_kao,",
        "n_f_lip_l,Hrothgar Cheek Left,Face,FALSE,FALSE,j_kao,n_f_lip_r",
        "n_f_lip_r,Hrothgar Cheek Right,Face,FALSE,FALSE,j_kao,n_f_lip_l",
        "n_f_ulip_l,Hrothgar Lip Upper Left,Face,FALSE,FALSE,j_kao,n_f_ulip_r",
        "n_f_ulip_r,Hrothgar Lip Upper Right,Face,FALSE,FALSE,j_kao,n_f_ulip_l",
        "j_f_dlip,Hrothgar Lip Lower,Face,FALSE,FALSE,j_kao,", "j_ago,Jaw,Face,TRUE,FALSE,j_kao,",
        "j_f_uago,Hrothgar Palate Upper,Face,FALSE,FALSE,j_kao,",
        "j_f_ulip,Hrothgar Palate Lower,Face,FALSE,FALSE,j_kao,",
        "j_mimi_l,Ear Left,Ears,TRUE,FALSE,j_kao,j_mimi_r", "j_mimi_r,Ear Right,Ears,TRUE,FALSE,j_kao,j_mimi_l",
        "j_zera_a_l,Viera Ear 01 A Left,Ears,FALSE,FALSE,j_kao,j_zera_a_r",
        "j_zera_a_r,Viera Ear 01 A Right,Ears,FALSE,FALSE,j_kao,j_zera_a_l",
        "j_zera_b_l,Viera Ear 01 B Left,Ears,FALSE,FALSE,j_kao,j_zera_b_r",
        "j_zera_b_r,Viera Ear 01 B Right,Ears,FALSE,FALSE,j_kao,j_zera_b_l",
        "j_zerb_a_l,Viera Ear 02 A Left,Ears,FALSE,FALSE,j_kao,j_zerb_a_r",
        "j_zerb_a_r,Viera Ear 02 A Right,Ears,FALSE,FALSE,j_kao,j_zerb_a_l",
        "j_zerb_b_l,Viera Ear 02 B Left,Ears,FALSE,FALSE,j_kao,j_zerb_b_r",
        "j_zerb_b_r,Viera Ear 02 B Right,Ears,FALSE,FALSE,j_kao,j_zerb_b_l",
        "j_zerc_a_l,Viera Ear 03 A Left,Ears,FALSE,FALSE,j_kao,j_zerc_a_r",
        "j_zerc_a_r,Viera Ear 03 A Right,Ears,FALSE,FALSE,j_kao,j_zerc_a_l",
        "j_zerc_b_l,Viera Ear 03 B Left,Ears,FALSE,FALSE,j_kao,j_zerc_b_r",
        "j_zerc_b_r,Viera Ear 03 B Right,Ears,FALSE,FALSE,j_kao,j_zerc_b_l",
        "j_zerd_a_l,Viera Ear 04 A Left,Ears,FALSE,FALSE,j_kao,j_zerd_a_r",
        "j_zerd_a_r,Viera Ear 04 A Right,Ears,FALSE,FALSE,j_kao,j_zerd_a_l",
        "j_zerd_b_l,Viera Ear 04 B Left,Ears,FALSE,FALSE,j_kao,j_zerd_b_r",
        "j_zerd_b_r,Viera Ear 04 B Right,Ears,FALSE,FALSE,j_kao,j_zerd_b_l",
        "j_sako_l,Clavicle Left,Chest,TRUE,FALSE,j_sebo_c,j_sako_r",
        "j_sako_r,Clavicle Right,Chest,TRUE,FALSE,j_sebo_c,j_sako_l",
        "j_mune_l,Breast Left,Chest,TRUE,FALSE,j_sebo_b,j_mune_r",
        "j_mune_r,Breast Right,Chest,TRUE,FALSE,j_sebo_b,j_mune_l",
        "iv_c_mune_l,Breast B Left,Chest,FALSE,TRUE,j_mune_l,iv_c_mune_r",
        "iv_c_mune_r,Breast B Right,Chest,FALSE,TRUE,j_mune_r,iv_c_mune_l",
        "n_hkata_l,Shoulder Left,Arms,TRUE,FALSE,j_ude_a_l,n_hkata_r",
        "n_hkata_r,Shoulder Right,Arms,TRUE,FALSE,j_ude_a_r,n_hkata_l",
        "j_ude_a_l,Arm Left,Arms,TRUE,FALSE,j_sako_l,j_ude_a_r",
        "j_ude_a_r,Arm Right,Arms,TRUE,FALSE,j_sako_r,j_ude_a_l",
        "iv_nitoukin_l,Bicep Left,Arms,FALSE,TRUE,j_ude_a_l,iv_nitoukin_r",
        "iv_nitoukin_r,Bicep Right,Arms,FALSE,TRUE,j_ude_a_r,iv_nitoukin_l",
        "n_hhiji_l,Elbow Left,Arms,TRUE,FALSE,j_ude_b_l,n_hhiji_r",
        "n_hhiji_r,Elbow Right,Arms,TRUE,FALSE,j_ude_b_r,n_hhiji_l",
        "j_ude_b_l,Forearm Left,Arms,TRUE,FALSE,j_ude_a_l,j_ude_b_r",
        "j_ude_b_r,Forearm Right,Arms,TRUE,FALSE,j_ude_a_r,j_ude_b_l",
        "n_hte_l,Wrist Left,Arms,TRUE,FALSE,j_ude_b_l,n_hte_r",
        "n_hte_r,Wrist Right,Arms,TRUE,FALSE,j_ude_b_r,n_hte_l", "j_te_l,Hand Left,Hands,TRUE,FALSE,n_hte_l,j_te_r",
        "j_te_r,Hand Right,Hands,TRUE,FALSE,n_hte_r,j_te_l",
        "j_oya_a_l,Thumb A Left,Hands,TRUE,FALSE,j_te_l,j_oya_a_r",
        "j_oya_a_r,Thumb A Right,Hands,TRUE,FALSE,j_te_r,j_oya_a_l",
        "j_oya_b_l,Thumb B Left,Hands,TRUE,FALSE,j_oya_a_l,j_oya_b_r",
        "j_oya_b_r,Thumb B Right,Hands,TRUE,FALSE,j_oya_a_r,j_oya_b_l",
        "j_hito_a_l,Index A Left,Hands,TRUE,FALSE,j_te_l,j_hito_a_r",
        "j_hito_a_r,Index A Right,Hands,TRUE,FALSE,j_te_r,j_hito_a_l",
        "j_hito_b_l,Index B Left,Hands,TRUE,FALSE,j_hito_a_l,j_hito_b_r",
        "j_hito_b_r,Index B Right,Hands,TRUE,FALSE,j_hito_a_r,j_hito_b_l",
        "j_naka_a_l,Middle A Left,Hands,TRUE,FALSE,j_te_l,j_naka_a_r",
        "j_naka_a_r,Middle A Right,Hands,TRUE,FALSE,j_te_r,j_naka_a_l",
        "j_naka_b_l,Middle B Left,Hands,TRUE,FALSE,j_naka_a_l,j_naka_b_r",
        "j_naka_b_r,Middle B Right,Hands,TRUE,FALSE,j_naka_a_r,j_naka_b_l",
        "j_kusu_a_l,Ring A Left,Hands,TRUE,FALSE,j_te_l,j_kusu_a_r",
        "j_kusu_a_r,Ring A Right,Hands,TRUE,FALSE,j_te_r,j_kusu_a_l",
        "j_kusu_b_l,Ring B Left,Hands,TRUE,FALSE,j_kusu_a_l,j_kusu_b_r",
        "j_kusu_b_r,Ring B Right,Hands,TRUE,FALSE,j_kusu_a_r,j_kusu_b_l",
        "j_ko_a_l,Pinky A Left,Hands,TRUE,FALSE,j_te_l,j_ko_a_r",
        "j_ko_a_r,Pinky A Right,Hands,TRUE,FALSE,j_te_r,j_ko_a_l",
        "j_ko_b_l,Pinky B Left,Hands,TRUE,FALSE,j_ko_a_l,j_ko_b_r",
        "j_ko_b_r,Pinky B Right,Hands,TRUE,FALSE,j_ko_a_r,j_ko_b_l",
        "iv_hito_c_l,Index C Left,Hands,FALSE,TRUE,j_hito_b_l,iv_hito_c_r",
        "iv_hito_c_r,Index C Right,Hands,FALSE,TRUE,j_hito_b_r,iv_hito_c_l",
        "iv_naka_c_l,Middle C Left,Hands,FALSE,TRUE,j_naka_b_l,iv_naka_c_r",
        "iv_naka_c_r,Middle C Right,Hands,FALSE,TRUE,j_naka_b_r,iv_naka_c_l",
        "iv_kusu_c_l,Ring C Left,Hands,FALSE,TRUE,j_kusu_b_l,iv_kusu_c_r",
        "iv_kusu_c_r,Ring C Right,Hands,FALSE,TRUE,j_kusu_b_r,iv_kusu_c_l",
        "iv_ko_c_l,Pinky C Left,Hands,FALSE,TRUE,j_ko_b_l,iv_ko_c_r",
        "iv_ko_c_r,Pinky C Right,Hands,FALSE,TRUE,j_ko_b_r,iv_ko_c_l", "n_sippo_a,Tail A,Tail,FALSE,FALSE,j_kosi,",
        "n_sippo_b,Tail B,Tail,FALSE,FALSE,n_sippo_a,", "n_sippo_c,Tail C,Tail,FALSE,FALSE,n_sippo_b,",
        "n_sippo_d,Tail D,Tail,FALSE,FALSE,n_sippo_c,", "n_sippo_e,Tail E,Tail,FALSE,FALSE,n_sippo_d,",
        "iv_shiri_l,Buttock Left,Groin,FALSE,TRUE,j_kosi,iv_shiri_r",
        "ya_shiri_phys_l,Lower Buttock Left,Groin,TRUE,TRUE,j_kosi,ya_shiri_phys_r",
        "iv_shiri_r,Buttock Right,Groin,FALSE,TRUE,j_kosi,iv_shiri_l",
        "ya_shiri_phys_r,Lower Buttock Right,Groin,TRUE,TRUE,j_kosi,ya_shiri_phys_l",
        "iv_kougan_l,Scrotum Left,Groin,FALSE,TRUE,iv_ochinko_a,iv_kougan_r",
        "iv_kougan_r,Scrotum Right,Groin,FALSE,TRUE,iv_ochinko_a,iv_kougan_l",
        "iv_ochinko_a,Penis A,Groin,FALSE,TRUE,j_kosi,", "iv_ochinko_b,Penis B,Groin,FALSE,TRUE,iv_ochinko_a,",
        "iv_ochinko_c,Penis C,Groin,FALSE,TRUE,iv_ochinko_b,",
        "iv_ochinko_d,Penis D,Groin,FALSE,TRUE,iv_ochinko_c,",
        "iv_ochinko_e,Penis E,Groin,FALSE,TRUE,iv_ochinko_d,",
        "iv_ochinko_f,Penis F,Groin,FALSE,TRUE,iv_ochinko_e,", "iv_omanko,Vagina,Groin,FALSE,TRUE,j_kosi,",
        "iv_kuritto,Clitoris,Groin,FALSE,TRUE,iv_omanko,",
        "iv_inshin_l,Labia Left,Groin,FALSE,TRUE,iv_omanko,iv_inshin_r",
        "iv_inshin_r,Labia Right,Groin,FALSE,TRUE,iv_omanko,iv_inshin_l", "iv_koumon,Anus,Groin,FALSE,TRUE,j_kosi,",
        "iv_koumon_l,Anus B Right,Groin,FALSE,TRUE,iv_koumon,iv_koumon_r",
        "iv_koumon_r,Anus B Left,Groin,FALSE,TRUE,iv_koumon,iv_koumon_l",
        "j_asi_a_l,Leg Left,Legs,TRUE,FALSE,j_kosi,j_asi_a_r",
        "ya_daitai_phys_l,Front Thigh Left,Legs,TRUE,FALSE,j_asi_a_l,ya_daitai_phys_r",
        "iv_daitai_phys_l,Back Thigh Left,Legs,TRUE,FALSE,j_asi_a_l,iv_daitai_phys_r",
        "j_asi_a_r,Leg Right,Legs,TRUE,FALSE,j_kosi,j_asi_a_l",
        "ya_daitai_phys_r,Front Thigh Right,Legs,TRUE,FALSE,j_asi_a_r,ya_daitai_phys_l",
        "iv_daitai_phys_r,Back Thigh Right,Legs,TRUE,FALSE,j_asi_a_r,iv_daitai_phys_l",
        "j_asi_b_l,Knee Left,Legs,TRUE,FALSE,j_asi_a_l,j_asi_b_r",
        "j_asi_b_r,Knee Right,Legs,TRUE,FALSE,j_asi_a_r,j_asi_b_l",
        "j_asi_c_l,Calf Left,Legs,TRUE,FALSE,j_asi_b_l,j_asi_c_r",
        "j_asi_c_r,Calf Right,Legs,TRUE,FALSE,j_asi_b_r,j_asi_c_l",
        "j_asi_d_l,Foot Left,Feet,TRUE,FALSE,j_asi_c_l,j_asi_d_r",
        "j_asi_d_r,Foot Right,Feet,TRUE,FALSE,j_asi_c_r,j_asi_d_l",
        "j_asi_e_l,Toes Left,Feet,TRUE,FALSE,j_asi_d_l,j_asi_e_r",
        "j_asi_e_r,Toes Right,Feet,TRUE,FALSE,j_asi_d_r,j_asi_e_l",
        "iv_asi_oya_a_l,Big Toe A Left,Feet,FALSE,TRUE,j_asi_e_l,iv_asi_oya_a_r",
        "iv_asi_oya_a_r,Big Toe A Right,Feet,FALSE,TRUE,j_asi_e_r,iv_asi_oya_a_l",
        "iv_asi_oya_b_l,Big Toe B Left,Feet,FALSE,TRUE,iv_asi_oya_a_l,iv_asi_oya_b_r",
        "iv_asi_oya_b_r,Big Toe B Right,Feet,FALSE,TRUE,iv_asi_oya_a_r,iv_asi_oya_b_l",
        "iv_asi_hito_a_l,Index Toe A Left,Feet,FALSE,TRUE,j_asi_e_l,iv_asi_hito_a_r",
        "iv_asi_hito_a_r,Index Toe A Right,Feet,FALSE,TRUE,j_asi_e_r,iv_asi_hito_a_l",
        "iv_asi_hito_b_l,Index Toe B Left,Feet,FALSE,TRUE,iv_asi_hito_a_l,iv_asi_hito_b_r",
        "iv_asi_hito_b_r,Index Toe B Right,Feet,FALSE,TRUE,iv_asi_hito_a_r,iv_asi_hito_b_l",
        "iv_asi_naka_a_l,Middle Toe A Left,Feet,FALSE,TRUE,j_asi_e_l,iv_asi_naka_a_r",
        "iv_asi_naka_a_r,Middle Toe A Right,Feet,FALSE,TRUE,j_asi_e_r,iv_asi_naka_a_l",
        "iv_asi_naka_b_l,Middle Toe B Left,Feet,FALSE,TRUE,iv_asi_naka_a_l,iv_asi_naka_b_r",
        "iv_asi_naka_b_r,Middle Toe B Right,Feet,FALSE,TRUE,iv_asi_naka_a_r,iv_asi_naka_b_l",
        "iv_asi_kusu_a_l,Fore Toe A Left,Feet,FALSE,TRUE,j_asi_e_l,iv_asi_kusu_a_r",
        "iv_asi_kusu_a_r,Fore Toe A Right,Feet,FALSE,TRUE,j_asi_e_r,iv_asi_kusu_a_l",
        "iv_asi_kusu_b_l,Fore Toe B Left,Feet,FALSE,TRUE,iv_asi_kusu_a_l,iv_asi_kusu_b_r",
        "iv_asi_kusu_b_r,Fore Toe B Right,Feet,FALSE,TRUE,iv_asi_kusu_a_r,iv_asi_kusu_b_l",
        "iv_asi_ko_a_l,Pinky Toe A Left,Feet,FALSE,TRUE,j_asi_e_l,iv_asi_ko_a_r",
        "iv_asi_ko_a_r,Pinky Toe A Right,Feet,FALSE,TRUE,j_asi_e_r,iv_asi_ko_a_l",
        "iv_asi_ko_b_l,Pinky Toe B Left,Feet,FALSE,TRUE,iv_asi_ko_a_l,iv_asi_ko_b_r",
        "iv_asi_ko_b_r,Pinky Toe B Right,Feet,FALSE,TRUE,iv_asi_ko_a_r,iv_asi_ko_b_l",
        "j_ex_met_va,Visor,Hat,FALSE,FALSE,j_kao,", "j_ex_met_a,Hat Accessory A,Hat,FALSE,FALSE,j_kao,",
        "j_ex_met_b,Hat Accessory B,Hat,FALSE,FALSE,j_kao,",
        "n_ear_b_l,Earring B Left,Earrings,FALSE,FALSE,n_ear_a_l,n_ear_b_r",
        "n_ear_b_r,Earring B Right,Earrings,FALSE,FALSE,n_ear_a_r,n_ear_b_l",
        "n_ear_a_l,Earring A Left,Earrings,FALSE,FALSE,j_kao,n_ear_a_r",
        "n_ear_a_r,Earring A Right,Earrings,FALSE,FALSE,j_kao,n_ear_a_l",
        "j_ex_top_a_l,Cape A Left,Cape,FALSE,FALSE,j_sebo_c,j_ex_top_a_r",
        "j_ex_top_a_r,Cape A Right,Cape,FALSE,FALSE,j_sebo_c,j_ex_top_a_l",
        "j_ex_top_b_l,Cape B Left,Cape,FALSE,FALSE,j_ex_top_a_l,j_ex_top_b_r",
        "j_ex_top_b_r,Cape B Right,Cape,FALSE,FALSE,j_ex_top_a_r,j_ex_top_b_l",
        "n_kataarmor_l,Pauldron Left,Armor,FALSE,FALSE,n_hkata_l,n_kataarmor_r",
        "n_kataarmor_r,Pauldron Right,Armor,FALSE,FALSE,n_hkata_r,n_kataarmor_l",
        "n_hijisoubi_l,Elbow Plate Left,Armor,FALSE,FALSE,n_hhiji_l,n_hijisoubi_r",
        "n_hijisoubi_r,Elbow Plate Right,Armor,FALSE,FALSE,n_hhiji_r,n_hijisoubi_l",
        "n_hizasoubi_l,Knee Plate Left,Armor,FALSE,FALSE,j_asi_b_l,n_hizasoubi_r",
        "n_hizasoubi_r,Knee Plate Right,Armor,FALSE,FALSE,j_asi_b_r,n_hizasoubi_l",
        "j_sk_b_a_l,Skirt Back A Left,Skirt,FALSE,FALSE,j_kosi,j_sk_b_a_r",
        "j_sk_b_a_r,Skirt Back A Right,Skirt,FALSE,FALSE,j_kosi,j_sk_b_a_l",
        "j_sk_b_b_l,Skirt Back B Left,Skirt,FALSE,FALSE,j_sk_b_a_l,j_sk_b_b_r",
        "j_sk_b_b_r,Skirt Back B Right,Skirt,FALSE,FALSE,j_sk_b_a_r,j_sk_b_b_l",
        "j_sk_b_c_l,Skirt Back C Left,Skirt,FALSE,FALSE,j_sk_b_b_l,j_sk_b_c_r",
        "j_sk_b_c_r,Skirt Back C Right,Skirt,FALSE,FALSE,j_sk_b_b_r,j_sk_b_c_l",
        "j_sk_f_a_l,Skirt Front A Left,Skirt,FALSE,FALSE,j_kosi,j_sk_f_a_r",
        "j_sk_f_a_r,Skirt Front A Right,Skirt,FALSE,FALSE,j_kosi,j_sk_f_a_l",
        "j_sk_f_b_l,Skirt Front B Left,Skirt,FALSE,FALSE,j_sk_f_a_l,j_sk_f_b_r",
        "j_sk_f_b_r,Skirt Front B Right,Skirt,FALSE,FALSE,j_sk_f_a_r,j_sk_f_b_l",
        "j_sk_f_c_l,Skirt Front C Left,Skirt,FALSE,FALSE,j_sk_f_b_l,j_sk_f_c_r",
        "j_sk_f_c_r,Skirt Front C Right,Skirt,FALSE,FALSE,j_sk_f_b_r,j_sk_f_c_l",
        "j_sk_s_a_l,Skirt Side A Left,Skirt,FALSE,FALSE,j_kosi,j_sk_s_a_r",
        "j_sk_s_a_r,Skirt Side A Right,Skirt,FALSE,FALSE,j_kosi,j_sk_s_a_l",
        "j_sk_s_b_l,Skirt Side B Left,Skirt,FALSE,FALSE,j_sk_s_a_l,j_sk_s_b_r",
        "j_sk_s_b_r,Skirt Side B Right,Skirt,FALSE,FALSE,j_sk_s_a_r,j_sk_s_b_l",
        "j_sk_s_c_l,Skirt Side C Left,Skirt,FALSE,FALSE,j_sk_s_b_l,j_sk_s_c_r",
        "j_sk_s_c_r,Skirt Side C Right,Skirt,FALSE,FALSE,j_sk_s_b_r,j_sk_s_c_l",
        "n_throw,Throw,Root,FALSE,FALSE,j_kosi,",
        "j_buki_sebo_l,Scabbard Left,Equipment,FALSE,FALSE,j_kosi,j_buki_sebo_r",
        "j_buki_sebo_r,Scabbard Right,Equipment,FALSE,FALSE,j_kosi,j_buki_sebo_l",
        "j_buki2_kosi_l,Holster Left,Equipment,FALSE,FALSE,j_kosi,j_buki2_kosi_r",
        "j_buki2_kosi_r,Holster Right,Equipment,FALSE,FALSE,j_kosi,j_buki2_kosi_l",
        "j_buki_kosi_l,Sheath Left,Equipment,FALSE,FALSE,j_kosi,j_buki_kosi_r",
        "j_buki_kosi_r,Sheath Right,Equipment,FALSE,FALSE,j_kosi,j_buki_kosi_l",
        "n_buki_tate_l,Shield Left,Equipment,FALSE,FALSE,n_hte_l,n_buki_tate_r",
        "n_buki_tate_r,Shield Right,Equipment,FALSE,FALSE,n_hte_r,n_buki_tate_l",
        "n_buki_l,Weapon Left,Equipment,FALSE,FALSE,j_te_l,n_buki_r",
        "n_buki_r,Weapon Right,Equipment,FALSE,FALSE,j_te_r,n_buki_l",

        "j_f_face,Face Root (Dawntrail),Face,TRUE,FALSE,j_kao,",
        "j_f_hana,Nose,Face,TRUE,FALSE,j_kao,",
        "j_f_hana_l,Nose Left,Face,TRUE,FALSE,j_f_hana,j_f_hana_r",
        "j_f_hana_r,Nose Right,Face,TRUE,FALSE,j_f_hana,j_f_hana_l",
        "j_f_uhana,Bridge,Face,TRUE,FALSE,j_f_hana,",
        "j_f_hoho_l,Cheek Left,Cheeks,TRUE,FALSE,j_f_face,j_f_hoho_r",
        "j_f_hoho_r,Cheek Right,Cheeks,TRUE,FALSE,j_f_face,j_f_hoho_l",
        "j_f_dhoho_l,Outer Cheek Left,Cheeks,TRUE,FALSE,j_f_face,j_f_dhoho_r",
        "j_f_dhoho_r,Outer Cheek Right,Cheeks,TRUE,FALSE,j_f_face,j_f_dhoho_l",
        "j_f_shoho_l,Middle Cheek Left,Cheeks,TRUE,FALSE,j_f_face,j_f_shoho_r",
        "j_f_shoho_r,Middle Cheek Right,Cheeks,TRUE,FALSE,j_f_face,j_f_shoho_l",
        "j_f_dmemoto_l,Inner Cheek Left,Cheeks,TRUE,FALSE,j_f_face,j_f_dmemoto_r",
        "j_f_dmemoto_r,Inner Cheek Right,Cheeks,TRUE,FALSE,j_f_face,j_f_dmemoto_l",
        "j_f_dmiken_l,Glabella Left,Face,TRUE,FALSE,j_f_face,j_f_dmiken_r",
        "j_f_dmiken_r,Glabella Right,Face,TRUE,FALSE,j_f_face,j_f_dmiken_l",

        "j_f_ago,Jaw,Jaw,TRUE,FALSE,j_f_face,",
        "j_f_dago,Lower Jaw,Jaw,TRUE,FALSE,j_f_face,",
        "j_f_hagukiup,Upper Teeth,Jaw,TRUE,FALSE,j_f_face,",
        "j_f_hagukidn,Lower Teeth,Jaw,TRUE,FALSE,j_f_face,",
        "j_f_bero_01,Tongue A,Tongue,TRUE,FALSE,j_f_ago,",
        "j_f_bero_02,Tongue B,Tongue,TRUE,FALSE,j_f_bero_01,",
        "j_f_bero_03,Tongue C,Tongue,TRUE,FALSE,j_f_bero_02,",
        "j_f_dmlip_01_l,Lip Lower Left A,Lips,TRUE,FALSE,j_f_ago,j_f_dmlip_01_r",
        "j_f_dmlip_02_l,Lip Lower Left B,Lips,TRUE,FALSE,j_f_ago,j_f_dmlip_02_r",
        "j_f_umlip_01_l,Lip Upper Left A,Lips,TRUE,FALSE,j_f_ago,j_f_umlip_01_r",
        "j_f_umlip_02_l,Lip Upper Left B,Lips,TRUE,FALSE,j_f_ago,j_f_umlip_02_r",
        "j_f_dmlip_01_r,Lip Lower Right A,Lips,TRUE,FALSE,j_f_ago,j_f_dmlip_01_l",
        "j_f_dmlip_02_r,Lip Lower Right B,Lips,TRUE,FALSE,j_f_ago,j_f_dmlip_02_l",
        "j_f_umlip_01_r,Lip Upper Right A,Lips,TRUE,FALSE,j_f_ago,j_f_umlip_01_l",
        "j_f_umlip_02_r,Lip Upper Right B,Lips,TRUE,FALSE,j_f_ago,j_f_umlip_02_l",
        "j_f_dlip_01_l,Lip Lower Left Center A,Lips,TRUE,FALSE,j_f_ago,j_f_dlip_01_r",
        "j_f_dlip_02_l,Lip Lower Left Center B,Lips,TRUE,FALSE,j_f_ago,j_f_dlip_02_r",
        "j_f_ulip_01_l,Lip Upper Left Center A,Lips,TRUE,FALSE,j_f_ago,j_f_ulip_01_r",
        "j_f_ulip_02_l,Lip Upper Left Center B,Lips,TRUE,FALSE,j_f_ago,j_f_ulip_02_r",
        "j_f_dlip_01_r,Lip Lower Right Center A,Lips,TRUE,FALSE,j_f_ago,j_f_dlip_01_l",
        "j_f_dlip_02_r,Lip Lower Right Center B,Lips,TRUE,FALSE,j_f_ago,j_f_dlip_02_l",
        "j_f_ulip_01_r,Lip Upper Right Center A,Lips,TRUE,FALSE,j_f_ago,j_f_ulip_01_l",
        "j_f_ulip_02_r,Lip Upper Right Center B,Lips,TRUE,FALSE,j_f_ago,j_f_ulip_02_l",
        "j_f_uslip_l,Lip Upper Left Corner A,Lips,TRUE,FALSE,j_f_ago,j_f_uslip_r",
        "j_f_dslip_l,Lip Lower Left Corner A,Lips,TRUE,FALSE,j_f_ago,j_f_dslip_r",
        "j_f_uslip_r,Lip Upper Right Corner A,Lips,TRUE,FALSE,j_f_ago,j_f_uslip_l",
        "j_f_dslip_r,Lip Lower Right Corner A,Lips,TRUE,FALSE,j_f_ago,j_f_dslip_l",

        "j_f_mab_l,Eye Socket Left,Eyes,TRUE,FALSE,j_f_face,j_f_mab_r",
        "j_f_eyepuru_l,Iris Left,Eyes,TRUE,FALSE,j_f_face,j_f_eyepuru_r",
        "j_f_mabdn_01_l,Lower Eyelid Left,Eyes,TRUE,FALSE,j_f_face,j_f_mabdn_01_r",
        "j_f_mabup_01_l,Upper Eyelid Left,Eyes,TRUE,FALSE,j_f_face,j_f_mabup_01_r",
        "j_f_mabdn_02out_l,Lower Eyelid Outer Left,Eyes,TRUE,FALSE,j_f_face,j_f_mabdn_02out_r",
        "j_f_mabdn_03in_l,Lower Eyelid Inner Left,Eyes,TRUE,FALSE,j_f_face,j_f_mabdn_03in_r",
        "j_f_mabup_02out_l,Upper Eyelid Outer Left,Eyes,TRUE,FALSE,j_f_face,j_f_mabup_02out_r",
        "j_f_mabup_03in_l,Upper Eyelid Inner Left,Eyes,TRUE,FALSE,j_f_face,j_f_mabup_03in_r",
        "j_f_mab_r,Eye Socket Right,Eyes,TRUE,FALSE,j_f_face,j_f_mab_l",
        "j_f_eyepuru_r,Iris Right,Eyes,TRUE,FALSE,j_f_face,j_f_eyepuru_l",
        "j_f_mabdn_01_r,Lower Eyelid Right,Eyes,TRUE,FALSE,j_f_face,j_f_mabdn_01_l",
        "j_f_mabup_01_r,Upper Eyelid Right,Eyes,TRUE,FALSE,j_f_face,j_f_mabup_01_l",
        "j_f_mabdn_02out_r,Lower Eyelid Outer Right,Eyes,TRUE,FALSE,j_f_face,j_f_mabdn_02out_l",
        "j_f_mabdn_03in_r,Lower Eyelid Inner Right,Eyes,TRUE,FALSE,j_f_face,j_f_mabdn_03in_l",
        "j_f_mabup_02out_r,Upper Eyelid Outer Right,Eyes,TRUE,FALSE,j_f_face,j_f_mabup_02out_l",
        "j_f_mabup_03in_r,Upper Eyelid Inner Right,Eyes,TRUE,FALSE,j_f_face,j_f_mabup_03in_l",
        "j_f_mmayu_l,Eyebrow B Left,Eyes,TRUE,FALSE,j_f_face,j_f_mmayu_r",
        "j_f_miken_01_l,Brow Ridge A Left,Eyes,TRUE,FALSE,j_f_mmayu_l,j_f_miken_01_r",
        "j_f_miken_02_l,Brow Ridge B Left,Eyes,TRUE,FALSE,j_f_miken_01_l,j_f_miken_02_r",
        "j_f_mmayu_r,Eyebrow B Right,Eyes,TRUE,FALSE,j_f_face,j_f_mmayu_l",
        "j_f_miken_01_r,Brow Ridge A Right,Eyes,TRUE,FALSE,j_f_mmayu_r,j_f_miken_01_l",
        "j_f_miken_02_r,Brow Ridge B Right,Eyes,TRUE,FALSE,j_f_miken_01_r,j_f_miken_02_l",

        "iv_fukubu_phys,Upper Belly,Spine,TRUE,FALSE,j_sebo_a,",
        "ya_fukubu_phys,Lower Belly,Spine,TRUE,FALSE,j_kosi,",
        "iv_kyokin_phys_l,Chest Physics Left,Chest,FALSE,FALSE,j_sebo_b,iv_kyokin_phys_r",
        "iv_kyokin_phys_r,Chest Physics Right,Chest,FALSE,FALSE,j_sebo_b,iv_kyokin_phys_l",
        "iv_fukubu_phys_l,Belly Physics Left,Spine,FALSE,FALSE,j_sebo_a,iv_fukubu_phys_r",
        "iv_fukubu_phys_r,Belly Physics Right,Spine,FALSE,FALSE,j_sebo_a,iv_fukubu_phys_l",
        "iv_kintama_phys_l,Scrotum Physics Left,Groin,FALSE,FALSE,iv_kougan_l,iv_kintama_phys_r",
        "iv_kintama_phys_r,Scrotum Physics Right,Groin,FALSE,FALSE,iv_kougan_r,iv_kintama_phys_l",
        "iv_funyachin_phy_a,Flaccid Penis Physics A,Groin,FALSE,FALSE,j_kosi,",
        "iv_funyachin_phy_b,Flaccid Penis Physics B,Groin,FALSE,FALSE,iv_funyachin_phy_a,",
        "iv_funyachin_phy_c,Flaccid Penis Physics C,Groin,FALSE,FALSE,iv_funyachin_phy_b,",
        "iv_funyachin_phy_d,Flaccid Penis Physics D,Groin,FALSE,FALSE,iv_funyachin_phy_c,",
        "nf_bulge_a,NFLB Bulge,Groin,FALSE,FALSE,j_kosi,",
        "nf_nipple_l,NFLB Nipple Left,Chest,FALSE,FALSE,iv_c_mune_l,nf_nipple_r",
        "nf_nipple_r,NFLB Nipple Right,Chest,FALSE,FALSE,iv_c_mune_r,nf_nipple_l",
        "nf_clitoris,NFLB Clitoris,Groin,FALSE,FALSE,iv_kuritto,",
        "nf_labia_inner_l,NFLB Inner Labia Left,Groin,FALSE,FALSE,iv_inshin_l,nf_labia_inner_r",
        "nf_labia_inner_r,NFLB Inner Labia Right,Groin,FALSE,FALSE,iv_inshin_r,nf_labia_inner_l",
        "nf_labia_outer_l,NFLB Outer Labia Left,Groin,FALSE,FALSE,iv_inshin_l,nf_labia_outer_r",
        "nf_labia_outer_r,NFLB Outer Labia Right,Groin,FALSE,FALSE,iv_inshin_r,nf_labia_outer_l",
        "butt_left,Skelomae Buttock Left,Groin,FALSE,FALSE,,butt_right",
        "butt_right,Skelomae Buttock Right,Groin,FALSE,FALSE,,butt_left",
        "thigh_l,Skelomae Thigh Left,Legs,FALSE,FALSE,,thigh_r",
        "thigh_r,Skelomae Thigh Right,Legs,FALSE,FALSE,,thigh_l",
        "belly_sebo_a,Skelomae Belly Spine,Spine,FALSE,FALSE,,",
        "belly_kosi,Skelomae Belly Waist,Spine,FALSE,FALSE,,",
        "forebreas_l,Skelomae Forebreast Left,Chest,FALSE,FALSE,,forebreas_r",
        "forebreas_r,Skelomae Forebreast Right,Chest,FALSE,FALSE,,forebreas_l",
        "tongue_a,Skelomae Tongue A,Tongue,FALSE,FALSE,,", "tongue_b,Skelomae Tongue B,Tongue,FALSE,FALSE,tongue_a,",
        "tongue_c,Skelomae Tongue C,Tongue,FALSE,FALSE,tongue_b,", "tongue_d,Skelomae Tongue D,Tongue,FALSE,FALSE,tongue_c,",
        "tongue_e,Skelomae Tongue E,Tongue,FALSE,FALSE,tongue_d,",
        "mkl_wingbase_l,Skelomae Wing Base Left,Equipment,FALSE,FALSE,,mkl_wingbase_r",
        "mkl_wingbase_r,Skelomae Wing Base Right,Equipment,FALSE,FALSE,,mkl_wingbase_l",
        "mkl_wingarm_a_l,Skelomae Wing Arm A Left,Equipment,FALSE,FALSE,mkl_wingbase_l,mkl_wingarm_a_r",
        "mkl_wingarm_a_r,Skelomae Wing Arm A Right,Equipment,FALSE,FALSE,mkl_wingbase_r,mkl_wingarm_a_l",
        "mkl_wingarm_b_l,Skelomae Wing Arm B Left,Equipment,FALSE,FALSE,mkl_wingarm_a_l,mkl_wingarm_b_r",
        "mkl_wingarm_b_r,Skelomae Wing Arm B Right,Equipment,FALSE,FALSE,mkl_wingarm_a_r,mkl_wingarm_b_l",
        "mkl_wingarm_c_l,Skelomae Wing Arm C Left,Equipment,FALSE,FALSE,mkl_wingarm_b_l,mkl_wingarm_c_r",
        "mkl_wingarm_c_r,Skelomae Wing Arm C Right,Equipment,FALSE,FALSE,mkl_wingarm_b_r,mkl_wingarm_c_l",
        "mkl_wingarm_d_l,Skelomae Wing Arm D Left,Equipment,FALSE,FALSE,mkl_wingarm_c_l,mkl_wingarm_d_r",
        "mkl_wingarm_d_r,Skelomae Wing Arm D Right,Equipment,FALSE,FALSE,mkl_wingarm_c_r,mkl_wingarm_d_l",
    };

    public static readonly Dictionary<BoneFamily, string?> DisplayableFamilies = new()
    {
        { BoneFamily.Cheeks, null },
        { BoneFamily.Jaw, null },
        { BoneFamily.Tongue, null },
        { BoneFamily.Lips, null },
        { BoneFamily.Eyes, null },
        { BoneFamily.Spine, null },
        { BoneFamily.Hair, null },
        { BoneFamily.Face, null },
        { BoneFamily.Ears, null },
        { BoneFamily.Chest, null },
        { BoneFamily.Arms, null },
        { BoneFamily.Hands, null },
        { BoneFamily.Tail, null },
        { BoneFamily.Groin, "NSFW IVCS Compatible Bones" },
        { BoneFamily.Legs, null },
        { BoneFamily.Feet, null },
        { BoneFamily.Earrings, "Some mods utilize these bones for their physics properties" },
        { BoneFamily.Hat, null },
        { BoneFamily.Cape, "Some mods utilize these bones for their physics properties" },
        { BoneFamily.Armor, null },
        { BoneFamily.Skirt, null },
        { BoneFamily.Equipment, "These may behave oddly" },
        {
            BoneFamily.Unknown,
            "These bones weren't immediately identifiable.\nIf you can figure out what they're for, let us know and we'll add them to the table."
        }
    };

    private static readonly Dictionary<string, BoneDatum> BoneTable = new();

    private static readonly Dictionary<string, string> BoneLookupByDispName = new();

    private static readonly Dictionary<BoneFamily, string[]> BoneFamilySearchAliases = new()
    {
        [BoneFamily.Root] = ["root", "base", "global"],
        [BoneFamily.Spine] = ["waist", "torso", "abdomen", "belly", "stomach", "neck", "pelvis", "hips"],
        [BoneFamily.Chest] = ["chest", "bust", "breast", "upper chest", "ribcage"],
        [BoneFamily.Arms] = ["shoulder", "shoulders", "shoulder width", "clavicle", "upper arm", "bicep", "forearm", "elbow", "wrist"],
        [BoneFamily.Hands] = ["hand", "hands", "finger", "fingers", "palm"],
        [BoneFamily.Tail] = ["tail", "butt", "glute", "glutes"],
        [BoneFamily.Groin] = ["groin", "pelvis", "hips", "butt", "glute"],
        [BoneFamily.Legs] = ["leg", "legs", "hip", "hips", "pelvis", "thigh", "knee", "calf", "shin"],
        [BoneFamily.Feet] = ["foot", "feet", "ankle", "toe", "toes"],
        [BoneFamily.Hair] = ["hair", "bangs", "ponytail"],
        [BoneFamily.Face] = ["face", "head"],
        [BoneFamily.Ears] = ["ear", "ears"],
        [BoneFamily.Cheeks] = ["cheek", "cheeks"],
        [BoneFamily.Jaw] = ["jaw", "chin", "mouth"],
        [BoneFamily.Tongue] = ["tongue", "mouth"],
        [BoneFamily.Lips] = ["lip", "lips", "mouth"],
        [BoneFamily.Eyes] = ["eye", "eyes", "brow", "eyebrow"],
        [BoneFamily.Equipment] = ["equipment", "helper", "clothing"],
        [BoneFamily.Unknown] = ["unknown", "custom", "manual", "experimental", "modded"]
    };

    static BoneData()
    {
        //apparently static constructors are only guaranteed to START before the class is called
        //which can apparently lead to race conditions, as I've found out
        //this lock is to make sure the table is fully initialized before anything else can try to look at it
        lock (BoneTable)
        {
            var rowIndex = 0;
            foreach (var entry in BoneRawTable)
            {
                try
                {
                    var cells = entry.Split(',');
                    var codename = cells[0];
                    var dispName = cells[1];

                    if (BoneTable.ContainsKey(codename))
                        throw new InvalidOperationException($"Duplicate canonical bone name '{codename}'.");

                    var datum = new BoneDatum(rowIndex, cells);
                    datum.Metadata = ResolveMetadata(codename, datum);
                    BoneTable.Add(codename, datum);
                    BoneLookupByDispName[dispName] = codename;

                    if (BoneTable[codename].Family == BoneFamily.Unknown)
                    {
                        throw new Exception("what the fuck?");
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidCastException($"Couldn't parse raw bone table @ row {rowIndex}", ex);
                }

                ++rowIndex;
            }

            //iterate through the complete collection and assign children to their parents
            foreach (var kvp in BoneTable)
            {
                var datum = BoneTable[kvp.Key];

                datum.Children = BoneTable.Where(x => x.Value.Parent == kvp.Key).Select(x => x.Key).ToArray();

                BoneTable[kvp.Key] = datum;
            }
        }
    }

    public static void LogNewBones(params string[] boneNames)
    {
        lock (BoneTable)
        {
            var canonicalNames = boneNames
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(CuratedBoneRegistry.Canonicalize)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var probablyHairstyleBones = canonicalNames.Where(IsProbablyHairstyle).ToArray();

            foreach (var hairBone in ParseHairstyle(probablyHairstyleBones))
            {
                var datum = hairBone;
                datum.Metadata = ResolveMetadata(datum.Codename, datum);
                BoneTable[datum.Codename] = datum;
                BoneLookupByDispName[datum.DisplayName] = datum.Codename;
            }

            foreach (var boneName in canonicalNames.Except(BoneTable.Keys))
            {
                var datum = new BoneDatum
                {
                    RowIndex = int.MaxValue,
                    Codename = boneName,
                    DisplayName = boneName,
                    Family = boneName.StartsWith("nf_", StringComparison.Ordinal) ? BoneFamily.Equipment : BoneFamily.Unknown,
                    IsDefault = false,
                    IsIVCSCompatible = false,
                    Parent = null,
                    Children = Array.Empty<string>(),
                    MirroredCodename = null
                };
                datum.Metadata = ResolveMetadata(boneName, datum);
                datum.DisplayName = datum.Metadata.Origin == BoneOrigin.UnknownCustom
                    ? $"Unknown ({boneName})"
                    : $"{datum.Metadata.Origin} ({boneName})";
                BoneTable[boneName] = datum;
                BoneLookupByDispName[datum.DisplayName] = boneName;
            }
        }
    }

    public static void UpdateParentage(string parentName, string childName)
    {
        var child = BoneTable[childName];
        var parent = BoneTable[parentName];

        child.Parent = parentName;
        parent.Children = parent.Children.Append(childName).Distinct().ToArray();

        BoneTable[childName] = child;
        BoneTable[parentName] = parent;
    }

    public static string GetBoneDisplayName(string codename)
    {
        var canonical = CuratedBoneRegistry.Canonicalize(codename);
        return BoneTable.TryGetValue(canonical, out var row) ? row.DisplayName : canonical;
    }

    public static string? GetBoneCodename(string boneDisplayName)
    {
        return BoneLookupByDispName.TryGetValue(boneDisplayName, out var name) ? name : null;
    }

    public static List<string> GetBoneCodenames()
    {
        return BoneTable.Keys.ToList();
    }

    public static List<string> GetBoneCodenames(Func<BoneDatum, bool> predicate)
    {
        return BoneTable.Where(x => predicate(x.Value)).Select(x => x.Key).ToList();
    }

    public static List<string> GetBoneDisplayNames()
    {
        return BoneLookupByDispName.Keys.ToList();
    }

    public static BoneFamily GetBoneFamily(string codename)
    {
        return BoneTable.TryGetValue(CuratedBoneRegistry.Canonicalize(codename), out var row) ? row.Family : BoneFamily.Unknown;
    }

    public static string GetCanonicalBoneName(string codename)
        => CuratedBoneRegistry.Canonicalize(codename);

    public static BoneMetadata GetMetadata(string codename)
    {
        var canonical = CuratedBoneRegistry.Canonicalize(codename);
        if (BoneTable.TryGetValue(canonical, out var row))
            return row.Metadata;

        return CuratedBoneRegistry.InferKnownExtension(canonical);
    }

    public static bool HasAutomationTrust(string codename, BoneAutomationTrust trust)
        => GetMetadata(codename).HasTrust(trust);

    public static bool IsUnknownCustomBone(string codename)
        => GetMetadata(codename).Origin == BoneOrigin.UnknownCustom;

    public static SkeletonCapabilityManifest EvaluateCapabilities(IEnumerable<string> liveBoneNames)
        => CuratedBoneRegistry.EvaluateCapabilities(liveBoneNames, GetMetadata);

    /// <summary>
    /// Evaluates the published live topology for diagnostics only. The result does not alter bone trust or runtime behavior.
    /// </summary>
    public static SkeletonCapabilityManifest EvaluateCapabilityManifest(
        IEnumerable<ObservedSkeletonBone> observedBones,
        IReadOnlyList<int> partialBoneCounts,
        long revision,
        int stableObservations,
        bool bindingCurrent)
        => SkeletonCapabilityManifestEvaluator.Evaluate(
            observedBones,
            partialBoneCounts,
            revision,
            stableObservations,
            bindingCurrent,
            GetMetadata,
            GetCanonicalBoneName);

    public static bool IsDefaultBone(string codename)
    {
        return BoneTable.TryGetValue(CuratedBoneRegistry.Canonicalize(codename), out var row) && row.IsDefault;
    }

    public static int GetBoneRanking(string codename)
    {
        return BoneTable.TryGetValue(codename, out var row) ? row.RowIndex : 0;
    }

    public static bool IsIVCSCompatibleBone(string codename)
    {
        return BoneTable.TryGetValue(CuratedBoneRegistry.Canonicalize(codename), out var row) && row.IsIVCSCompatible;
    }

    public static string? GetBoneMirror(string codename)
    {
        return BoneTable.TryGetValue(CuratedBoneRegistry.Canonicalize(codename), out var row) ? row.MirroredCodename : null;
    }

    public static string? GetAutomationMirror(string codename)
    {
        var mirror = GetBoneMirror(codename);
        return mirror != null
               && HasAutomationTrust(codename, BoneAutomationTrust.MirrorSafe)
               && HasAutomationTrust(mirror, BoneAutomationTrust.MirrorSafe)
            ? mirror
            : null;
    }

    public static string[] GetChildren(string codename)
    {
        return BoneTable.TryGetValue(codename, out var row) ? row.Children : Array.Empty<string>();
    }

    /// <summary>
    /// Validates the static, advisory registry. Live armature topology is deliberately not checked here.
    /// </summary>
    public static IReadOnlyList<string> ValidateRegistry()
    {
        var issues = new List<string>();
        foreach (var (name, datum) in BoneTable)
        {
            if (datum.MirroredCodename is { } mirror)
            {
                if (mirror == name)
                    issues.Add($"{name} mirrors itself.");
                else if (!BoneTable.TryGetValue(mirror, out var mirrorDatum))
                    issues.Add($"{name} references missing mirror {mirror}.");
                else if (mirrorDatum.MirroredCodename != name)
                    issues.Add($"{name} and {mirror} are not symmetric mirrors.");
            }

            if (datum.Metadata.ScalingInheritance.SourceBone is { } source && !BoneTable.ContainsKey(source))
                issues.Add($"{name} references missing scaling source {source}.");
            if (datum.Parent is { } parent && !BoneTable.ContainsKey(parent))
                issues.Add($"{name} references missing advisory parent {parent}.");
        }

        foreach (var (alias, canonical) in CuratedBoneRegistry.KnownAliases)
        {
            if (alias == canonical)
                issues.Add($"Alias {alias} resolves to itself.");
            else if (!BoneTable.ContainsKey(canonical))
                issues.Add($"Alias {alias} references missing canonical bone {canonical}.");
            else if (CuratedBoneRegistry.TryGetAliasTarget(canonical, out _))
                issues.Add($"Alias {alias} resolves through another alias.");
        }

        foreach (var name in BoneTable.Keys)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = name;
            while (BoneTable.TryGetValue(current, out var datum) && datum.Parent is { } parent)
            {
                if (!visited.Add(current))
                {
                    issues.Add($"Advisory parent cycle includes {name}.");
                    break;
                }

                current = parent;
            }

            var metadata = BoneTable[name].Metadata;
            if (metadata.Origin == BoneOrigin.UnknownCustom && metadata.Trust != BoneAutomationTrust.ManualOnly)
                issues.Add($"Unknown/custom bone {name} has automation trust.");
            if (metadata.Role is BoneFunctionalRole.ClothingRig or BoneFunctionalRole.PropRig
                && metadata.HasTrust(BoneAutomationTrust.SemanticSafe | BoneAutomationTrust.PropagationSafe))
            {
                issues.Add($"{name} grants body automation to a clothing or prop rig.");
            }
            if (metadata.ScalingInheritance.Mode == BoneScalingInheritanceMode.None
                && metadata.ScalingInheritance.SourceBone != null)
            {
                issues.Add($"{name} has a scaling source without an inheritance mode.");
            }
        }

        return issues;
    }

    public static bool MatchesSearch(string codename, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        var query = search.Trim();
        if (codename.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            GetBoneDisplayName(codename).Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var family = GetBoneFamily(codename);
        if (family.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        return BoneFamilySearchAliases.TryGetValue(family, out var aliases) &&
            aliases.Any(alias =>
                alias.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                query.Contains(alias, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsProbablyHairstyle(string codename)
    {
        return Regex.IsMatch(codename, @"j_ex_h\d\d\d\d_ke_[abcdeflrsu](_[abcdeflrsu])?");
    }

    public static bool IsNewBone(string codename)
    {
        return !BoneTable.ContainsKey(CuratedBoneRegistry.Canonicalize(codename));
    }

    private static BoneFamily ParseFamilyName(string n)
    {
        var simplified = n.Split(' ').FirstOrDefault()?.ToLower() ?? string.Empty;

        var fam = simplified switch
        {
            "root" => BoneFamily.Root,
            "spine" => BoneFamily.Spine,
            "hair" => BoneFamily.Hair,
            "face" => BoneFamily.Face,
            "ears" => BoneFamily.Ears,
            "chest" => BoneFamily.Chest,
            "arms" => BoneFamily.Arms,
            "hands" => BoneFamily.Hands,
            "tail" => BoneFamily.Tail,
            "groin" => BoneFamily.Groin,
            "legs" => BoneFamily.Legs,
            "feet" => BoneFamily.Feet,
            "earrings" => BoneFamily.Earrings,
            "hat" => BoneFamily.Hat,
            "cape" => BoneFamily.Cape,
            "armor" => BoneFamily.Armor,
            "skirt" => BoneFamily.Skirt,
            "cheeks" => BoneFamily.Cheeks,
            "equipment" => BoneFamily.Equipment,
            "jaw" => BoneFamily.Jaw,
            "tongue" => BoneFamily.Tongue,
            "lips" => BoneFamily.Lips,
            "eyes" => BoneFamily.Eyes,
            _ => BoneFamily.Unknown
        };

        return fam;
    }

    public struct BoneDatum : IComparable<BoneDatum>
    {
        public int RowIndex;

        public string Codename;
        public string DisplayName;
        public BoneFamily Family;

        public bool IsDefault;
        public bool IsIVCSCompatible;

        public string? Parent;
        public string? MirroredCodename;

        public string[] Children;
        public BoneMetadata Metadata;

        public BoneDatum(int row, string[] fields)
        {
            RowIndex = row;

            var i = 0;

            Codename = fields[i++];
            DisplayName = fields[i++];

            Family = ParseFamilyName(fields[i++]);

            IsDefault = bool.Parse(fields[i++]);
            IsIVCSCompatible = bool.Parse(fields[i++]);

            Parent = string.IsNullOrEmpty(fields[i]) ? null : fields[i];
            i++;
            MirroredCodename = string.IsNullOrEmpty(fields[i]) ? null : fields[i];
            i++;

            Children = Array.Empty<string>();
            Metadata = BoneMetadata.Unknown;
        }

        public int CompareTo(BoneDatum other)
        {
            return RowIndex != other.RowIndex
                ? RowIndex.CompareTo(other.RowIndex)
                : string.Compare(DisplayName, other.DisplayName, StringComparison.Ordinal);
        }
    }

    #region hair stuff

    private static IEnumerable<BoneDatum> ParseHairstyle(params string[] boneNames)
    {
        List<BoneDatum> output = new();

        var index = 0;
        foreach (var style in boneNames.GroupBy(x => Regex.Match(x, @"\d\d\d\d").Value))
        {
            try
            {
                var parsedBones = style.Select(ParseHairBone).ToArray();

                // if any of the first subs is nonstandard letter, we can presume that any bcd... are part of a rising sequence
                var firstAsc =
                    parsedBones.Any(x => x.sub1 is "a" or "c" or "d" or "e");
                //and we can then presume that the second subs are directional
                //or vice versa. the naming conventions aren't really consistent about whether the sequence is first or second

                foreach (var boneInfo in parsedBones)
                {
                    StringBuilder dispName = new();
                    dispName.Append($"Hair #{boneInfo.id}");

                    var sub1 = GetHairBoneSubLabel(boneInfo.sub1, firstAsc);
                    var sub2 = boneInfo.sub2 == null ? null : GetHairBoneSubLabel(boneInfo.sub2, !firstAsc);

                    dispName.Append($" {sub1}");
                    if (sub2 != null)
                    {
                        dispName.Append($" {sub2}");
                    }

                    var result = new BoneDatum
                    {
                        RowIndex = -1,
                        Codename = boneInfo.name,
                        DisplayName = dispName.ToString(),
                        Family = BoneFamily.Hair,
                        IsDefault = false,
                        IsIVCSCompatible = false,
                        Parent = "j_kao",
                        Children = Array.Empty<string>(),
                        MirroredCodename = null
                    };

                    result.Metadata = ResolveMetadata(result.Codename, result);

                    output.Add(result);
                }
            }
            catch (Exception)
            {
                Plugin.Logger.Error($"Failed to dynamically parse bones for hairstyle of '{boneNames[index]}'");
            }

            index++;
        }

        return output;
    }

    private static BoneMetadata ResolveMetadata(string codename, BoneDatum datum)
    {
        if (CuratedBoneRegistry.TryGet(codename, out var curated))
            return curated;

        if (codename.StartsWith("nf_", StringComparison.Ordinal))
            return CuratedBoneRegistry.InferKnownExtension(codename);

        if (datum.Family == BoneFamily.Unknown)
            return BoneMetadata.Unknown;

        var role = codename.Contains("_ex", StringComparison.Ordinal)
            ? BoneFunctionalRole.ConditionalExtra
            : datum.Family switch
        {
            BoneFamily.Root or BoneFamily.Spine or BoneFamily.Chest or BoneFamily.Arms or BoneFamily.Hands or BoneFamily.Legs or BoneFamily.Feet or BoneFamily.Tail or BoneFamily.Groin => BoneFunctionalRole.StructuralAnatomical,
            BoneFamily.Face => BoneFunctionalRole.FacePrimary,
            BoneFamily.Eyes or BoneFamily.Cheeks or BoneFamily.Jaw or BoneFamily.Lips or BoneFamily.Tongue => BoneFunctionalRole.FaceHelper,
            BoneFamily.Equipment or BoneFamily.Hat or BoneFamily.Cape or BoneFamily.Armor or BoneFamily.Skirt or BoneFamily.Earrings => BoneFunctionalRole.GearAttachment,
            _ => BoneFunctionalRole.AnimationHelper,
        };
        var availability = codename.Contains("_ex", StringComparison.Ordinal)
            ? BoneAvailability.GearConditional | BoneAvailability.ConditionalExtra
            : role is BoneFunctionalRole.FacePrimary or BoneFunctionalRole.FaceHelper
                ? BoneAvailability.GPoseOrCutsceneOnly
                : BoneAvailability.Gameplay;
        var trust = datum.IsDefault && role == BoneFunctionalRole.StructuralAnatomical
            ? BoneAutomationTrust.MirrorSafe | BoneAutomationTrust.PropagationSafe | BoneAutomationTrust.TemplateSafe | BoneAutomationTrust.SemanticSafe | BoneAutomationTrust.AdvancedCorrectiveSafe
            : datum.IsDefault
                ? BoneAutomationTrust.MirrorSafe | BoneAutomationTrust.TemplateSafe
                : BoneAutomationTrust.TemplateSafe;
        return new BoneMetadata(BoneOrigin.Vanilla, role, availability, trust, BoneAnimationCompatibility.VanillaBaseline, BoneScalingInheritance.None, datum.Parent);
    }

    private static (string name, int id, string sub1, string? sub2) ParseHairBone(string boneName)
    {
        var groups = Regex.Match(boneName.ToLower(), @"j_ex_h(\d\d\d\d)_ke_([abcdeflrsu])(?:_([abcdeflrsu]))?")
            .Groups;

        var idNo = int.Parse(groups[1].Value);
        var subFirst = groups[2].Value;
        var subSecond = string.IsNullOrWhiteSpace(groups[3].Value) ? null : groups[3].Value;

        return (boneName, idNo, subFirst, subSecond);
    }

    private static string GetHairBoneSubLabel(string sub, bool ascending)
    {
        return (sub.ToLower(), ascending) switch
        {
            ("a", _) => "A",
            ("b", true) => "B",
            ("b", false) => "Back",
            ("c", _) => "C",
            ("d", _) => "D",
            ("e", _) => "E",
            ("f", true) => "F",
            ("f", false) => "Front",
            ("l", _) => "Left",
            ("r", _) => "Right",
            ("u", _) => "Upper",
            ("s", _) => "Side",
            (_, true) => "Next",
            (_, false) => "Bone"
        };
    }

    #endregion
}
