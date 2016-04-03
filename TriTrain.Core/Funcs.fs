﻿namespace TriTrain.Core

open Reflection

module Id =
  let create =
    let r = ref 0
    let f () =
      (! r)
      |> tap (fun i -> r := i + 1)
      |> Id
    in f

module PlayerId =
  let all =
    DU<PlayerId>.UnitCases

  let inverse =
    function
    | PlLft -> PlRgt
    | PlRgt -> PlLft

module CardId =
  let id          (cardId: CardId) = cardId.Id
  let owner       (cardId: CardId) = cardId.Owner

  let create plId =
    {
      Owner       = plId
      Id          = Id.create ()
    }

module Elem =
  let all =
    DU<Elem>.UnitCases

  let isStrongTo src tar =
    match (src, tar) with
    | (Air  , Fire )
    | (Fire , Water)
    | (Water, Earth)
    | (Earth, Air  ) -> true
    | _ -> false

  /// 属性 src が属性 tar を攻撃するときにかかる係数
  let coeff src tar =
    if   isStrongTo src tar then 1.5
    elif isStrongTo tar src then 0.5
    else 1.0

module Vertex =
  let all =
    DU<Vertex>.UnitCases

module Row =
  let all =
    DU<Row>.UnitCases

  let ofVertex =
    function
    | Fwd -> FwdRow
    | Lft
    | Rgt -> BwdRow

module ScopeSide =
  let all =
    DU<ScopeSide>.UnitCases

module Scope =
  let name ((name, _): NamedScope) = name

  /// plId からみた side 側のリスト
  let sides plId side =
    match side with
    | Home -> [plId]
    | Oppo -> [plId |> PlayerId.inverse]
    | Both -> PlayerId.all

  let rec placeSet ((plId, vx) as source) scope: Set<Place> =
    match scope with
    | AbsScope (homeSet, oppoSet) ->
        [
          for p in homeSet -> (plId, p)
          for p in oppoSet -> (plId |> PlayerId.inverse, p)
        ]
        |> Set.ofList

    | FwdSide side ->
        match vx |> Row.ofVertex with
        | FwdRow -> Set.empty
        | BwdRow ->
            [ for pi in sides plId side -> (pi, Fwd) ]
            |> Set.ofList

    | BwdSide side ->
        match vx |> Row.ofVertex with
        | FwdRow ->
            [
              for pi in sides plId side do
                for p in [Lft; Rgt] -> (pi, p)
            ]
            |> Set.ofList
        | BwdRow -> Set.empty

    | LftSide side ->
        let sides = sides plId side
        let fwd =
          match vx with
          | Lft -> []
          | Fwd | Rgt -> [ for side in sides -> (side, Fwd) ]
        let lft =
          match vx with
          | Lft | Fwd -> []
          | Rgt -> [ for side in sides -> (side, Lft) ]
        in (List.append fwd lft) |> Set.ofList

    | RgtSide side ->
        let sides = sides plId side
        let fwd =
          match vx with
          | Lft | Fwd -> [ for side in sides -> (side, Fwd) ]
          | Rgt -> []
        let rgt =
          match vx with
          | Lft -> [ for side in sides -> (side, Rgt) ]
          | Fwd | Rgt -> []
        in  (List.append fwd rgt) |> Set.ofList

    | Self ->
        Set.singleton source

    | FrontEnemy ->
        Set.singleton ((PlayerId.inverse plId), vx)

    | UnionScope scopes ->
        scopes
        |> List.map (placeSet source)
        |> Set.unionMany

module KEffect =
  let typ         (keff: KEffect) = keff.Type
  let duration    (keff: KEffect) = keff.Duration

module OEffect =
  let name ((name, _): NamedOEffect) = name

  let rec toList oeff =
    match oeff with
    | OEffectList oeffs -> oeffs |> List.collect toList
    | _ -> [oeff]

module Status =
  let hp (st: Status) = st.HP
  let at (st: Status) = st.AT
  let ag (st: Status) = st.AG

  let toList st =
    [ st |> hp; st |> at; st |> ag ]

  let total st =
    st |> toList |> List.sum

module CardSpec =
  let name        (spec: CardSpec) = spec.Name
  let status      (spec: CardSpec) = spec.Status
  let elem        (spec: CardSpec) = spec.Elem
  let abils       (spec: CardSpec) = spec.Abils
  let skills      (spec: CardSpec) = spec.Skills

module Card =
  let cardId      (card: Card) = card.CardId
  let spec        (card: Card) = card.Spec
  let curHp       (card: Card) = card.CurHP
  let effects     (card: Card) = card.Effects

  let create spec cardId =
    {
      CardId        = cardId
      Spec          = spec
      CurHP         = spec |> CardSpec.status |> Status.hp
      Effects       = []
    }

  let owner =
    cardId >> CardId.owner

  let name =
    spec >> CardSpec.name

  let elem =
    spec >> CardSpec.elem

  let maxHp =
    spec >> CardSpec.status >> Status.hp

  let isAlive card =
    curHp card > 0

  let curAt card =
    card
    |> effects
    |> List.map (fun keff ->
        match keff |> KEffect.typ with
        | ATInc (One, value) -> value
        | _ -> 0.0
        )
    |> List.sum
    |> (+) (card |> spec |> CardSpec.status |> Status.at |> float)
    |> int

  let curAg card =
    card
    |> effects
    |> List.map (fun keff ->
        match keff |> KEffect.typ with
        | AGInc (One, value) -> value
        | _ -> 0.0
        )
    |> List.sum
    |> (+) (card |> spec |> CardSpec.status |> Status.ag |> float)
    |> int

  let curStatus card =
    {
      HP = card |> curHp 
      AT = card |> curAt
      AG = card |> curAg
    }

  /// 再生効果を適用する
  let regenerate card =
    let (regenValues, effects') =
      card
      |> effects
      |> List.paritionMap
          (function
          | { Type = (Regenerate (One, value)) } -> Some value
          | _ -> None
          )
    let card =
      { card with
          CurHP     = regenValues |> List.sum |> int |> max 0
          Effects   = effects'
      }
    in card

module Amount =
  /// 変量を決定する
  let rec resolve (actor: option<Card>) (amount: Amount) =
    let rate = amount |> snd
    let value =
      match amount |> fst with
      | One -> 1.0
      | MaxHP ->
          match actor with
          | Some actor -> actor |> Card.maxHp |> float
          | None -> 0.0
      | AT ->
          match actor with
          | Some actor -> actor |> Card.curAt |> float
          | None -> 0.0
    in value * rate

  let resolveKEffect actorOpt target keff =
    match keff |> KEffect.typ with
    | ATInc amount ->
        { keff with Type = ATInc (One, amount |> resolve actorOpt) }
    | AGInc amount ->
        { keff with Type = AGInc (One, amount |> resolve actorOpt) }
    | Regenerate amount ->
        // 対象者の最大HPに依存する
        { keff with Type = Regenerate (One, amount |> resolve (Some target)) }

module DeckSpec =
  let name        (spec: DeckSpec) = spec.Name
  let cards       (spec: DeckSpec) = spec.Cards

module Deck =
  let create (spec) (plId) =
    spec
    |> T7.toList
    |> List.map (fun cardSpec ->
        let cardId = CardId.create plId
        in Card.create cardSpec cardId
        )

module Board =
  /// 空き頂点をつぶしてカードを移動させるために必要な、
  /// 具体的なカードの移動を計算する。
  let rotate (board: Board): list<CardId * Vertex * Vertex> =
    board
    |> Map.toList
    |> List.sortBy fst    // 位置順
    |> List.zipShrink Vertex.all
    |> List.choose
        (fun (v', (v, cardId)) ->
            if v = v'
            then None
            else Some (cardId, v, v')
            )

  let emptyVertexSet board =
    board
    |> Map.keySet
    |> Set.difference (Vertex.all |> Set.ofList)

module PlayerSpec =
  let name        (spec: PlayerSpec) = spec.Name
  let deck        (spec: PlayerSpec) = spec.Deck

module Player =
  let playerId    (pl: Player) = pl.PlayerId
  let spec        (pl: Player) = pl.Spec
  let deck        (pl: Player) = pl.Deck
  let board       (pl: Player) = pl.Board
  let trash       (pl: Player) = pl.Trash

  let create spec plId =
    let deck' =
      Deck.create (spec |> PlayerSpec.deck |> DeckSpec.cards) plId
    let pl =
      {
        PlayerId      = plId
        Spec          = spec
        Deck          = deck' |> List.map (Card.cardId)
        Board         = Map.empty
        Trash         = Set.empty
      }
    in (pl, deck')

  let name =
    spec >> PlayerSpec.name
