using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace ATU.Trazabilidad.Core
{
    public class ValidadorEtiquetas
    {
        public class ResultadoDeteccion
        {
            public string Recibo { get; set; }
            public string Producto { get; set; }
            public string Tarima { get; set; }
            public string Tipo { get; set; }
            public bool EsValido { get; set; }
            public string BatchId => $"{Recibo}-{Producto}-{Tarima}";
        }

        public ResultadoDeteccion Procesar(string raw, DataTable catalogo)
        {
            if (string.IsNullOrWhiteSpace(raw) || catalogo == null)
                return new ResultadoDeteccion { EsValido = false };

            string etiqueta = raw.Trim().ToUpper();

            // REGLA DE ORO: El catálogo DEBE venir ordenado por LEN(prod_clave) DESC
            foreach (DataRow row in catalogo.Rows)
            {
                string clave = row["prod_clave"].ToString().Trim().ToUpper();

                if (etiqueta.Contains(clave))
                {
                    int indexProd = etiqueta.IndexOf(clave);
                    string tipo = row["prod_tipo"].ToString().Trim();

                    // Extracción por anclaje
                    string reciboRaw = etiqueta.Substring(0, indexProd).TrimStart('0');
                    string tarimaRaw = etiqueta.Substring(indexProd + clave.Length);

                    // Lógica especial para tus casos de Recibo (5) y Tarima (2)
                    if (reciboRaw.Length == 5 && tarimaRaw.Length >= 2)
                    {
                        tarimaRaw = tarimaRaw.Substring(0, 2);
                    }

                    return new ResultadoDeteccion
                    {
                        Recibo = reciboRaw,
                        Producto = clave,
                        Tarima = tarimaRaw,
                        Tipo = tipo,
                        EsValido = true
                    };
                }
            }

            return new ResultadoDeteccion { EsValido = false };
        }
    }
}