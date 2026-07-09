---
tags: [jaravi, negocio, documento, latex]
---

# Jaravi Paper (LaTeX)

Puente entre este vault y el documento formal de estrategia de producto y arquitectura, compilado en LaTeX/XeLaTeX.

> [!info] Ubicación del fuente
> `docs/jaravi.tex` en la raíz del repositorio (compila con `xelatex jaravi.tex`, dos pasadas para índice y referencias). PDF de salida: `docs/jaravi.pdf`.

## Contenido del documento

1. Resumen ejecutivo
2. El problema: colapso de contexto y explosión de costos → ver [[Investigacion de Mercado]]
3. El mercado → ver [[Investigacion de Mercado]]
4. Arquitectura: motor determinista, no otro agente → ver [[Arquitectura]]
5. Diferenciación técnica
6. Modelo de negocio: open-core → ver [[Modelo de Negocio]]
7. Roadmap
8. Conclusión

## Nota de motor tipográfico

El proyecto tenía un bootstrap de **ConTeXt LMTX** (`C:\Users\USER\context`) instalado solo parcialmente — únicamente `mtxrun.exe`, sin la distribución completa (requiere descarga de red no completada). Se usó **XeLaTeX** (MiKTeX, ya instalado y verificado) en su lugar: mismo control de diseño absoluto (tipografía del sistema vía `fontspec`, diagramas con TikZ, cajas con `tcolorbox`), sin la fragilidad de una instalación de red a mitad de sesión.

Véase también: [[Investigacion de Mercado]], [[Modelo de Negocio]], [[Home|Jaravi]]
