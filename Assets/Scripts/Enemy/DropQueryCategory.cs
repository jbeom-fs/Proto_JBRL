// 드랍 쿼리 전용 카테고리. 값은 ItemType과 미러(직렬화 호환), AnyEngraving만 추가분.
// ItemData는 이 enum을 쓰지 않는다. AnyEngraving은 쿼리만 가질 수 있는 값이다.
public enum DropQueryCategory
{
    Key = 0,
    Currency = 1,
    Consumable = 2,
    // 3 = 폐기된 Equipment. 결번 유지, 재사용 금지.
    Relic = 4,
    Material = 5,
    Soul = 6,
    Engraving = 7,
    PassiveEngraving = 8,
    AnyEngraving = 9
}
