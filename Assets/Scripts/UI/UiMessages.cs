public static class UiMessages
{
    public const string FormUnlockedFormat = "새로운 폼 해금: {0}";
    public const string FormNameSword = "검";
    public const string FormNameDagger = "단검";
    public const string FormNameFreischutz = "마탄";
    public const string FormNameParry = "패리";
    public const string FormDescriptionSword = "검 메시지";
    public const string FormDescriptionDagger = "대거 메시지";
    public const string FormDescriptionFreischutz = "마탄 메시지";
    public const string FormDescriptionParry = "패리 메세";

    public const string LockedSectionHeader = "ㅡㅡ ???? ㅡㅡ";
    public const string SoulStatSummaryFormat = "{0} Lv.{1}/{2}  비용 {3}";
    public const string SoulStatOwnedSuffixFormat = "  (보유 {0})";
    public const string PassiveSlotUnlockFormat = "패시브 슬롯 {0}/{1}  비용 {2}  보유 {3}";
    public const string PassiveSlotUnlockMaxFormat = "패시브 슬롯 {0}/{1}  MAX";
    public const string CostFree = "무료";
    public const string AllocationSummaryFormat = "배분 {0} / 필요 {1}";
    public const string EmptyDescription = "내용없음";
    public const string EmptyCoreEffect = "효과 없음";
    public const string AttackEffect = "공격";
    public const string DefenseEffect = "방어";
    public const string MaxHpEffect = "최대체력";
    public const string MoveSpeedEffect = "이동속도";
    public const string DiscardEngravingChangesConfirmation = "변경사항을 저장하지 않고 닫으시겠습니까?";
    public const string SaveEngravingChangesConfirmation = "변경사항을 저장하시겠습니까?\n이 층의 각인대는 소멸합니다.";
    public const string SelectEngravingSlotFirst = "슬롯을 먼저 선택하세요";
    public const string EngravingEquippedUnsaved = "장착 완료 (미저장)";
    public const string EngravingSlotEmpty = "빈 슬롯";
    public const string EngravingUnequippedUnsaved = "장착 해제 완료 (미저장)";
    public const string EngravingApplyFailed = "각인 적용 실패";
    public const string EmptyEngravingSlot = "(비어 있음)";
    public const string EngravingSlotFormat = "[{0}] {1}";
    public const string EngravingSlotLockedFormat = "[{0}] 잠김";
    public const string EngravingGradeFormat = "{0} [{1}]";
    public const string EngravingTypeActive = "액티브";
    public const string EngravingTypePassive = "패시브";
    public const string EngravingGradeFaint = "희미한";
    public const string EngravingGradeWhole = "온전한";
    public const string EngravingGradePrimordial = "태초의";
    public const string EngravingTooltipMetaFormat = "<size=70%>{0}</size>";
    public const string EngravingTooltipMetaWithGradeFormat = "<size=70%>{0} · {1}</size>";
    public const string RestAreaCoreMissing = "코어 없음";
    public const string RestAreaCurrencyMissing = "재화 없음";
    public const string RestAreaNotEnoughCurrency = "재화 부족";
    public const string RestAreaCoreUpdateBlocked = "코어 갱신 불가";
    public const string RestAreaPurchaseFailed = "강화 실패";
    public const string RestAreaPurchasedFormat = "{0} 강화 완료";
    public const string RestAreaCurrencyFormat = "재화 {0}";
    public const string RestAreaNoOffers = "상품 없음";
    public const string RestAreaOfferLevelFormat = "Lv {0}  합계 +{1}";
    public const string RestAreaOfferCostFormat = "비용 {0}";
    public const string MagazineReloadingFormat = "재장전 중... {0}/{1}";
    public const string MagazineCountFormat = "{0}/{1}";

    public static string GetFormName(PlayerFormId form)
    {
        switch (form)
        {
            case PlayerFormId.Sword:
                return FormNameSword;
            case PlayerFormId.Dagger:
                return FormNameDagger;
            case PlayerFormId.Freischutz:
                return FormNameFreischutz;
            case PlayerFormId.Parry:
                return FormNameParry;
            default:
                return form.ToString();
        }
    }

    public static string GetEngravingGradeName(EngravingGrade grade)
    {
        switch (grade)
        {
            case EngravingGrade.Faint:
                return EngravingGradeFaint;
            case EngravingGrade.Whole:
                return EngravingGradeWhole;
            case EngravingGrade.Primordial:
                return EngravingGradePrimordial;
            default:
                return grade.ToString();
        }
    }

    public static string GetFormDescription(PlayerFormId form)
    {
        switch (form)
        {
            case PlayerFormId.Sword:
                return FormDescriptionSword;
            case PlayerFormId.Dagger:
                return FormDescriptionDagger;
            case PlayerFormId.Freischutz:
                return FormDescriptionFreischutz;
            case PlayerFormId.Parry:
                return FormDescriptionParry;
            default:
                return string.Empty;
        }
    }
}
