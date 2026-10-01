import type { ProductCard } from '../lib/types'

// Um card REAL, tirado da API em 25/09/2026 e enxugado (série de visitas cortada, fonte do
// regime encurtada). Vir do sistema, e não de um objeto escrito à mão, é o que garante que
// a fixture não envelheça mentindo: se o contrato mudar, o TypeScript reclama aqui.
//
// Escolhido por ser o caso que importa: sem fornecedor, sem margem — e com teto de compra.
export const cardSemFornecedor: ProductCard = {
    "productId": "MLB74451511",
    "name": "Pokémon ME05 Escuridão Absoluta Booster 6 Cartas Copag",
    "categoryId": "MLB1132",
    "cycleId": 4,
    "calculatedAt": "2026-09-25T23:03:19.968469+00:00",
    "quadrant": "EndOfCycle",
    "opportunityIndex": 0.0,
    "lowDemand": true,
    "explanation": "recebeu 1.791 visitas a menos que na semana anterior (−94%) · 71 vendedores, 5 novos na semana · 31% dos vendedores (16) praticam R$ 19",
    "sellers": 71,
    "newSellersThisWeek": 5,
    "price": {
      "status": "Calculated",
      "sampleSize": 51,
      "sellersFound": 71,
      "discarded": [
        {
          "listingId": "MLB7640775604",
          "sellerId": "54409293",
          "reason": "DuplicateSeller"
        },
        {
          "listingId": "MLB7525219084",
          "sellerId": "305645535",
          "reason": "DuplicateSeller"
        },
        {
          "listingId": "MLB5150391753",
          "sellerId": "48862155",
          "reason": "DuplicateSeller"
        },
        {
          "listingId": "MLB7580503502",
          "sellerId": "1737257283",
          "reason": "DuplicateSeller"
        },
        {
          "listingId": "MLB5238319819",
          "sellerId": "2323057123",
          "reason": "DuplicateSeller"
        }
      ],
      "officialStoreSellers": 0,
    "officialStoreShare": 0,
    "officialStorePrice": null,
    "sampleHash": "8F635558318E722E8372E959CAFB2D5CBA0A5A410BFFB44E79DF4BE242FB665C",
      "flags": "None",
      "marketPrice": 19,
      "source": "Mode",
      "mode": 19,
      "median": 19,
      "modeStrength": 0.3137254901960784,
      "modalBucketSellers": 16,
      "cleanRangeLow": 17,
      "cleanRangeHigh": 25,
      "suggestedShowcasePrice": 18.9,
      "dispersionIndex": 0.42105263157894735,
      "confidence": "Medium",
      "weeklyMarketPriceChange": null,
      "weeklyModeStrengthChange": null
    },
    "priceTrend": null,
    "priceUnstable": false,
    "feeCategoryId": "MLB6899",
    "demand": {
      "result": {
        "source": "Visits",
        "score": 0,
        "persistence": 0,
        "lastWeekVisits": 105,
        "previousWeekVisits": 1896,
        "absoluteDelta": -1791,
        "rate": -0.944620253164557,
        "acceleration": null,
        "risingWeeks": 0,
        "lowDemand": true,
        "falling": true,
        "currentPosition": null,
        "positionGain": null,
        "series": [
          {
            "date": "2026-09-10",
            "visits": 406
          },
          {
            "date": "2026-09-11",
            "visits": 300
          },
          {
            "date": "2026-09-12",
            "visits": 309
          }
        ]
      },
      "itemsInSeries": [
        "MLB4912100331",
        "MLB4906598261",
        "MLB4908339223",
        "MLB4966960939",
        "MLB4941580889"
      ],
      "fallbackReason": null,
    "growth": {
      "percent": -94.462,
      "isNewDemand": false,
      "lastWeekVisits": 105,
      "smallBase": false,
      "startedOn": null
    }
    },
    "margin": null,
    "risks": {
    "certifications": [
      {
        "categoryId": "MLB1132",
        "body": "Inmetro",
        "note": "Brinquedo exige certificação compulsória do Inmetro. Sem o selo o anúncio pode ser pausado e a carga retida."
      }
    ],
    "packagingPerUnit": 1.2,
    "breakageReserve": 0.02,
    "kits": [
      { "units": 2, "price": 40, "margin": 0.12, "marginPerUnitSale": 0.03, "gain": 0.09 },
      { "units": 5, "price": 100, "margin": 0.24, "marginPerUnitSale": 0.03, "gain": 0.21 }
    ]
  },
  "runway": {
    "runway": {
      "phase": "Decelerating",
      "lowerWeeks": 1,
      "upperWeeks": 4,
      "reason": "subiu +40%, mas o ritmo caiu — o pico ficou para trás"
    },
    "minimumSellingWindowDays": 30,
    "channels": [
      { "name": "Revenda nacional", "leadTimeDays": 5, "imported": false, "fits": false },
      { "name": "Importação aérea (Remessa Conforme)", "leadTimeDays": 25, "imported": true, "fits": false }
    ],
    "worthImporting": false
  },
  "ceiling": {
      "ceiling": {
        "atMarket": 9.975,
        "inRange": 8.925,
        "bindingPrice": 17,
        "targetMargin": 0.3,
        "prices": {
          "floor": 17,
          "market": 19,
          "ceiling": 25
        },
        "viable": true
      },
      "feeCategoryId": "MLB6899",
      "config": {
        "parametersFile": "configuracoes.json",
        "parametersValidFrom": "2026-09-24",
        "regime": "Remessa Conforme — pessoa física",
        "regimeValidFrom": "2026-05-12",
        "regimeFile": "configuracoes.json",
        "regimeSource": "Receita Federal"
      },
      "assessedAt": "2026-09-26T02:38:15.6036329+00:00"
    },
    "opportunity": {
      "index": 0,
      "demandFactor": 0,
      "supplyFactor": 0.9295774647887324,
      "pricePressure": 0.12631578947368421,
      "persistenceFactor": 0,
      "marginFactor": null,
      "includesMargin": false,
      "quadrant": "EndOfCycle"
    },
    "suppliers": []
  }

/**
 * O caso que motivou a correção: semana anterior zerada. O `rate` cru vale 19.989 (a proteção
 * contra divisão por zero do calculador), e era ele que virava "+1.998.900%" na tela.
 */
export const cardEstreia: ProductCard = {
  ...cardSemFornecedor,
  productId: 'MLB78092304',
  name: 'Brinquedo de Apertar Sensorial com Luz LED',
  explanation: 'estreou em 19/09 e fez 19.989 visitas em 7 dias · 6 vendedores, nenhum entrante desde a coleta anterior',
  demand: {
    ...cardSemFornecedor.demand,
    result: {
      ...cardSemFornecedor.demand.result,
      lastWeekVisits: 19989,
      previousWeekVisits: 0,
      absoluteDelta: 19989,
      rate: 19989,
      lowDemand: false
    },
    growth: { percent: null, isNewDemand: true, lastWeekVisits: 19989, smallBase: false, startedOn: '2026-09-19' }
  }
}

/** Base de 2 visitas: o percentual existe (+900%) e não significa nada (I11). */
export const cardBasePequena: ProductCard = {
  ...cardSemFornecedor,
  demand: {
    ...cardSemFornecedor.demand,
    result: { ...cardSemFornecedor.demand.result, lastWeekVisits: 20, previousWeekVisits: 2, absoluteDelta: 18, rate: 9 },
    growth: { percent: null, isNewDemand: false, lastWeekVisits: 20, smallBase: true, startedOn: null }
  }
}

export const cardComMargem: ProductCard = {
  ...cardSemFornecedor,
  margin: {
    supplierId: 1,
    supplierName: 'Yiwu Cases',
    supplierUnitPrice: 0.9,
    currency: 'USD',
    shipmentQuantity: 100,
    exchange: { quoteDate: '2026-09-25', ptax: 5.14, effectiveRate: 5.45 },
    landedCost: {
      regime: 'Remessa Conforme — pessoa física',
      customsValueForeign: 115,
      exceedsRegimeLimit: false,
      effectiveExchangeRate: 5.45,
      unitCost: 6,
      totalTaxes: 125.35,
      shipmentTotal: 600,
      quantity: 100,
      memo: [{ label: 'Produto', value: 490.5, formula: '0,90 × 100 × 5,45' }]
    },
    margin: {
      unitCost: 6,
      floor: { price: 17, saleCost: { price: 17, commission: 2.72, fixedFee: 6.75, absorbedFreight: 0, regimeTax: 1.02, total: 10.49 }, profit: 0.51, margin: 0.03 },
      market: { price: 19, saleCost: { price: 19, commission: 3.04, fixedFee: 6.75, absorbedFreight: 0, regimeTax: 1.14, total: 10.93 }, profit: 2.07, margin: 0.109 },
      ceiling: { price: 25, saleCost: { price: 25, commission: 4, fixedFee: 6.75, absorbedFreight: 0, regimeTax: 1.5, total: 12.25 }, profit: 6.75, margin: 0.27 },
      worstInRange: { price: 17, saleCost: { price: 17, commission: 2.72, fixedFee: 6.75, absorbedFreight: 0, regimeTax: 1.02, total: 10.49 }, profit: 0.51, margin: 0.03 },
      roiAtMarket: 0.345,
      breakEvenPrice: 16.3,
      lossZones: [],
      maxUnitCostAtMarket: 9.975,
      maxUnitCostInRange: 8.925,
      targetMargin: 0.3,
      worstBelowTarget: true
    },
    maxSupplierUnitPrice: 1.2,
    feeCategoryId: 'MLB6899',
    unavailable: null,
    config: cardSemFornecedor.ceiling!.config,
    assessedAt: '2026-09-25T23:30:00+00:00'
  }
}
